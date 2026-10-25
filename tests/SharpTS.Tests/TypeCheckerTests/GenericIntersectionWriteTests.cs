using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericIntersectionWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("function write<T>(box:T&{value:number}) { box.value=1; }", []),
            ("function write<T>(box:{value:number}&T) { box.value=1; }", []),
            ("function write<T>(box:T&{value:number}) { box.value='bad'; }", ["TS2322"]),
            ("function write<T>(box:T&{readonly value:number}) { box.value=1; }", ["TS2540"]),
            ("function write<T extends {value:number}>(box:T&{readonly value:number}) { box.value=1; }", []),
            ("function write<T extends {readonly value:number}>(box:T&{readonly value:number}) { box.value=1; }", ["TS2540"]),
            ("function write<T extends {value:1}>(box:T&{value:number}) { box.value=2; }", ["TS2322"]),
            ("function write<U,T extends {value:U}>(box:T&{value:number}) { box.value=1; }", ["TS2322"]),
            ("function write<T>(box:T&{value:number}) { box.missing=1; }", ["TS2339"]),
            ("function write<T>(box:T&{value?:number}) { box.value=undefined; box.value=1; }", []),
            ("function write<T>(box:T&{value?:number}) { box.value='bad'; }", ["TS2322"]),
            ("function write<T>(box:T&{value:number}) { box['value']=1; }", []),
            ("function write<T>(box:T&{value:number}) { box['value']='bad'; }", ["TS2322"]),
            ("function write<T>(box:T&{readonly value:number}) { box['value']=1; }", ["TS2540"]),
            ("function write<T>(box:T&{0:number}) { box[0]=1; }", []),
            ("function write<T>(box:T&{0:number}) { box[0]='bad'; }", ["TS2322"]),
            ("function write<T>(box:T&{a:number;b:number},key:'a'|'b') { box[key]=1; }", []),
            ("function write<T>(box:T&{a:number;b:string},key:'a'|'b') { box[key]=1; }", ["TS2322"]),
            ("function write<T>(box:T&{callback:(value:number)=>number}) { box.callback=value=>value+1; }", []),
            ("function write<T>(box:T&{callback:(value:number)=>number}) { box.callback=(value:string)=>1; }", ["TS2322"]),
            ("interface Value<U>{value:U;} function write<T>(box:T&Value<number>) { box.value=1; }", []),
            ("type Box<T> = T&{value:number}; function write<T>(box:Box<T>) { box.value=1; }", []),
            ("class Base { value=1; } function write<T>(box:T&Base) { box.value=2; }", []),
            ("class Base { readonly value=1; } function write<T>(box:T&Base) { box.value=1; }", ["TS2540"]),
            ("class Base { private value=1; } function write<T>(box:T&Base) { box.value=1; }", ["TS2341"]),
            ("class Base { protected value=1; } function write<T>(box:T&Base) { box.value=1; }", ["TS2445"]),
            ("class Base { get value():number{return 1;} } function write<T>(box:T&Base) { box.value=1; }", ["TS2540"]),
            ("class Base { get value():number{return 1;} set value(next:number|string){} } function write<T>(box:T&Base) { box.value='ok'; }", []),
            ("class Base<U> { value!:U; } class Child extends Base<number>{} function write<T>(box:T&Child) { box.value=1; }", []),
            ("class Base<U> { value!:U; } class Child extends Base<number>{} function write<T>(box:T&Child) { box.value='bad'; }", ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void KnownIntersectionMembersRetainWriteTypesAndModifiers(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "generic-intersection-write-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }

    [Theory, ModeData]
    public void IntersectionWritesExecuteWithVerifiedCompilerIl(ExecutionMode mode)
    {
        const string source = """
            function write<T>(box:T&{value:number}) { box.value=3; box['value']=4; }
            const box={value:1,label:'kept'};
            write(box);
            console.log(box.value);
            console.log(box.label);
            """;
        Assert.Equal("4\nkept\n", TestHarness.Run(source, mode));
    }
}
