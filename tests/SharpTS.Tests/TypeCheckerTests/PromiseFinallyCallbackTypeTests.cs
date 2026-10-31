using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public sealed class PromiseFinallyCallbackTypeTests
{
    [Theory,MemberData(nameof(PromiseFinallyCallbackTests.Cases),MemberType=typeof(PromiseFinallyCallbackTests))]
    public void RealDefaultLibraryAcceptsIgnoredCallbackResults(string name,string source,string expected)
    {
        Assert.NotEmpty(name);Assert.NotEmpty(expected);Assert.Empty(Check(source));
    }
    public static IEnumerable<object[]> Negative()
    {
        yield return ["noncallable-number","Promise.resolve(1).finally(2);","TS2345"];
        yield return ["noncallable-string","Promise.resolve(1).finally('cleanup');","TS2345"];
        yield return ["noncallable-object","Promise.resolve(1).finally({});","TS2345"];
        yield return ["required-callback-parameter","Promise.resolve(1).finally((value:number)=>value);","TS2345"];
        yield return ["extra-outer-argument","Promise.resolve(1).finally(()=>{},()=>{});","TS2554"];
        yield return ["promise-value-type-preserved","const result:Promise<string>=Promise.resolve(1).finally(()=>2);","TS2322"];
        yield return ["explicit-void-body","Promise.resolve(1).finally(():void=>2);","TS2322"];
        yield return ["nullable-result-type-preserved","const result:Promise<string>=Promise.resolve(1).finally(null);","TS2322"];
    }
    [Theory,MemberData(nameof(Negative))]
    public void RealDefaultLibraryPreservesCallbackAndResultRejections(string name,string source,string code)
    {
        Assert.NotEmpty(name);var diagnostic=Assert.Single(Check(source));Assert.Equal(code,diagnostic.TsCode);
    }
    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        string path=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"promise-finally-3279","main.ts"));
        var options=TypeScriptProgramOptions.Default with{Lib=["lib.es2022.d.ts","lib.dom.d.ts"],Types=[]};
        var resolver=new ModuleResolver(path,new Dictionary<string,string>{[path]=source},options);
        var entry=resolver.LoadProgram(path);var checker=new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry),resolver);return checker.GetDiagnostics();
    }
}
