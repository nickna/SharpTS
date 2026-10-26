using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NonExactOptionalWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, ordinary, exact) in new (string, string[], string[])[]
        {
            ("declare const box:{value?:number};box['value']=undefined;", [], ["TS2322"]),
            ("interface View{value?:number;}declare const box:View;box['value']=undefined;", [], ["TS2322"]),
            ("interface Base<T>{value?:T;}interface View extends Base<number>{}declare const box:View;box['value']=undefined;", [], ["TS2322"]),
            ("declare const box:{0?:number};box[0]=undefined;", [], ["TS2322"]),
            ("declare const box:{first?:number;second?:number};declare const key:'first'|'second';box[key]=undefined;", [], ["TS2322"]),
            ("declare const box:{first?:number;second:number};declare const key:'first'|'second';box[key]=undefined;", ["TS2322"], ["TS2322"]),
            ("declare const box:{value?:number};declare const value:number|undefined;box['value']=value;", [], ["TS2322"]),
            ("class View{static value?:number;}View.value=undefined;", [], ["TS2412"]),
            ("class Base{static value?:number;}class View extends Base{}View.value=undefined;", [], ["TS2412"]),
            ("class View<T>{static value?:number;}View.value=undefined;", [], ["TS2412"]),
            ("declare const box:{value?:number}|{value?:string};box.value=undefined;", [], ["TS2412"]),
            ("declare const box:{value?:number}|{value:number};box.value=undefined;", [], ["TS2412"]),
            ("interface A{value?:number;}interface B{value?:string;}declare const box:A|B;box.value=undefined;", [], ["TS2412"]),
            ("declare const box:{value?:number|undefined};box['value']=undefined;", [], []),
            ("class View{static value?:number|undefined;}View.value=undefined;", [], []),
            ("declare const box:{value?:number|undefined}|{value?:string|undefined};box.value=undefined;", [], []),
            ("declare const box:{value:number};box['value']=undefined;", ["TS2322"], ["TS2322"]),
            ("class View{static value:number=1;}View.value=undefined;", ["TS2322"], ["TS2322"]),
            ("declare const box:{value?:number};box['value']='wrong';", ["TS2322"], ["TS2322"]),
            ("class View{static value?:number;}View.value='wrong';", ["TS2322"], ["TS2322"]),
            ("declare const box:{value?:number}|{value?:string};box.value=true;", ["TS2322"], ["TS2322"]),
            ("declare const box:{readonly value?:number};box['value']=undefined;", ["TS2540"], ["TS2540"]),
            ("class View{static readonly value?:number;}View.value=undefined;", ["TS2540"], ["TS2540"]),
            ("class View{static set value(value:number){}}View.value=undefined;", ["TS2322"], ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true })
                foreach (bool exactOption in new[] { false, true })
                    yield return [source, exactOption ? exact : ordinary, modules, exactOption];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OptionalWritesUseTheSelectedPresencePolicyWithoutChangingRequiredSlots(
        string source, string[] codes, bool modules, bool exact)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { ExactOptionalPropertyTypes = exact });
        if (!modules)
        {
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
            return;
        }
        string path = Path.Combine(Path.GetTempPath(), "optional-write-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
    }

    public static IEnumerable<object[]> RuntimeCases()
    {
        foreach (string source in new[]
        {
            "const box:{value?:number}={value:1};box['value']=undefined;console.log(box.value===undefined);",
            "class View{static value?:number=1;}View.value=undefined;console.log(View.value===undefined);",
            "function clear(box:{value?:number}|{value?:string}){box.value=undefined;}const box={value:1};clear(box);console.log(box.value===undefined);",
        }) yield return [source];
    }

    public static IEnumerable<object[]> ParityCases() => RuntimeCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void AdmittedUndefinedWritesRemainObservable(string source, ExecutionMode mode)
        => Assert.Equal("true\n", TestHarness.Run(source, mode));

    [Theory, MemberData(nameof(RuntimeCases))]
    public void AdmittedUndefinedWritesHaveVerifiedIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
}
