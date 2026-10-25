using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericIntersectionCompoundWriteTests
{
    public static IEnumerable<object[]> ReadonlyWrites()
    {
        foreach (string operation in new[] { "box.value++", "++box.value", "box.value--", "--box.value", "box['value']++", "++box['value']",
            "box.value+=1", "box.value-=1", "box.value*=2", "box.value/=2", "box.value%=2", "box.value**=2", "box.value<<=1",
            "box.value>>=1", "box.value>>>=1", "box.value&=1", "box.value|=1", "box.value^=1", "box['value']+=1",
            "box.value&&=1", "box.value||=1", "box.value??=1", "box['value']&&=1" })
            foreach (bool modules in new[] { false, true })
                yield return [$"function write<T>(box:T&{{readonly value:number}}){{{operation};}}", "TS2540", modules];
        foreach (var (source, code) in new[]
        {
            ("function write<T>(box:T&{readonly 0:number}){box[0]++;}", "TS2540"),
            ("function write<T>(box:T&{readonly a:number;b:number},key:'a'|'b'){box[key]+=1;}", "TS2540"),
            ("interface Shape{readonly value:number;} function write<T>(box:T&Shape){box.value++;}", "TS2540"),
            ("interface Shape<U>{readonly value:U;} function write<T>(box:T&Shape<number>){box.value+=1;}", "TS2540"),
            ("type Shape<T>=T&{readonly value:number}; function write<T>(box:Shape<T>){box['value']++;}", "TS2540"),
            ("function write<T extends {readonly value:number}&{tag:string}>(box:T){box.value++;}", "TS2540"),
            ("class Box{readonly value=1;} function write<T>(box:T&Box){box.value++;}", "TS2540"),
            ("class Box{get value():number{return 1;}} function write<T>(box:T&Box){box.value+=1;}", "TS2540"),
            ("class Box{get value():number{return 1;} private set value(next:number){}} function write<T>(box:T&Box){box.value++;}", "TS2341")
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(ReadonlyWrites))]
    public void EveryReadModifyWriteChecksTheWritableMemberMetadata(string source, string code, bool modules)
        => Assert.Equal(code, Assert.Single(Check(source, modules)).TsCode);

    public static IEnumerable<object[]> MutableWrites()
    {
        foreach (string source in new[]
        {
            "function write<T>(box:T&{value:number}){box.value++; ++box.value; box.value+=1; box['value']+=1;}",
            "function write<T>(box:T&{0:number}){box[0]++; box[0]+=1;}",
            "function write<T>(box:T&{a:number;b:number},key:'a'|'b'){box[key]++; box[key]+=1;}",
            "function write<T extends {value:number}>(box:T&{readonly value:number}){box.value++; box.value+=1;}",
            "function write<T extends {readonly value:number}>(box:T&{value:number}){box.value++; box.value+=1;}",
            "class Box{get value():number{return 1;} set value(next:number|string){}} function write<T>(box:T&Box){box.value++; box.value+=1;}",
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(MutableWrites))]
    public void MutableConstituentsAndWritableAccessorsRemainWritable(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    [Theory, ModeData]
    public void ValidGenericUpdatesKeepRuntimeResultsAndVerifiedCompilerIl(ExecutionMode mode)
        => Assert.Equal("2\n4\n5\n", TestHarness.Run("function write<T>(box:T&{value:number}){console.log(box.value++); console.log(++box.value); box.value+=1; return box.value;} const box={value:2}; console.log(write(box));", mode));

    [Fact]
    public void BracketUpdatesKeepVerifiedCompiledBehavior()
        => Assert.Equal("2\n4\n5\n", TestHarness.Run("function write<T>(box:T&{value:number}){console.log(box.value++); console.log(++box['value']); box['value']+=1; return box.value;} const box={value:2}; console.log(write(box));", ExecutionMode.Compiled));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "intersection-compound-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
