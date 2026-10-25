using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericAccessorCallbackRefreshTests
{
    public static IEnumerable<object[]> ValidCalls()
    {
        foreach (string source in new[]
        {
            "declare function identity<T>(arg:T):T; const result=identity({get value(){return 1;}}); const value:number=result.value;",
            "declare function identity<T>(arg:T):T; const result=identity({set value(next:number){}}); result.value=2; const value:number=result.value;",
            "declare function identity<T>(arg:T):T; const result=identity({get value(){return 1;},set value(next:number){}}); result.value=2; const value:number=result.value;",
            "declare function identity<T>(arg:T):T; const result=identity({nested:{get value(){return 1;}}}); const value:number=result.nested.value;",
            "declare function identity<T>(arg:T):T; const result=identity({get callback(){return (x:number)=>x+1;}}); const value:number=result.callback(1);",
            "declare function identity<T>(arg:T):T; const result=identity({get value(){return 1;},method(x:number){return x+1;}}); const value:number=result.method(1);",
            "declare function use<T>(arg:{readonly value:T;callback:(value:T)=>void}):T; const value:number=use({get value(){return 1;},callback:x=>x.toFixed()});",
            "declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; const value:number=map(1,{transform:x=>x+1});",
            "declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; const value:number=map<number>(1,{transform:x=>x+1});",
            "declare function map<T>(...args:[T,{transform:(value:T)=>T}]):T; const value:number=map(1,{transform:x=>x+1});",
            "declare function map<T>(value:T,callbacks:((x:T)=>T)[]):T; const n:number=map(1,[x=>x+1]);"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidCalls))]
    public void RefreshPreservesAccessorValuesAndFinalContextualCallbackTypes(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstGenericAccessorResultsRetainReadonly(bool modules)
    {
        var diagnostics = Check("""
            declare function identity<const T>(arg:T):T;
            const result=identity({get value(){return 1;}});
            result.value=2;
            """, modules);
        Assert.Equal("TS2540", Assert.Single(diagnostics).TsCode);
    }

    public static IEnumerable<object[]> InvalidCallbacks()
    {
        foreach (string source in new[]
        {
            "declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; map<number>(1,{transform:(x:string)=>1});",
            "declare function map<T>(...args:[T,{transform:(value:T)=>T}]):T; map<number>(1,{transform:(x:string)=>1});"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(InvalidCallbacks))]
    public void RefreshRetainsExplicitCallbackParameterTypes(string source, bool modules)
        => Assert.Equal("TS2345", Assert.Single(Check(source, modules)).TsCode);

    [Theory]
    [InlineData("get value(){return 1;}", true)]
    [InlineData("set value(next:number){}", false)]
    [InlineData("get value(){return 1;},set value(next:number){}", false)]
    public void RefreshedArgumentRecordsKeepAccessorAndMethodMetadata(string accessor, bool getterOnly)
    {
        var statements = TestHarness.ParseOrThrow(
            "declare function identity<T>(arg:T):T; identity({" + accessor + ",method(x:number){return x+1;}});");
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var result = checker.CheckWithRecovery(statements);
        Assert.Empty(result.Diagnostics);
        var call = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(statements[^1]).Expr);
        var record = Assert.IsType<TypeInfo.Record>(result.TypeMap.Get(call.Arguments[0]));
        Assert.False(record.Fields["value"] is TypeInfo.Function);
        Assert.Equal(getterOnly, record.IsGetterOnly("value"));
        Assert.True(record.IsMethodMember("method"));
        Assert.IsType<TypeInfo.Function>(record.Fields["method"]);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "generic-accessor-refresh-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
