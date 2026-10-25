using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class IntersectionWritePrivateBrandTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, code) in new (string, string)[]
        {
            ("class X{private x=1;value=1;} class Y{private y=1;} class A{private x=1;value=1;} class B{private y=1;} type U=(X&B)|(A&Y); function f<T extends U>(box:T&X&Y){box.value=2;}", "TS2339"),
            ("class X{private x=1;value=1;} class Y{private y=1;} class A{private x=1;value=1;} type U=(X&Y)|(A&Y); function f<T extends U>(box:T&X&Y){box.value=2;}", ""),
            ("class A{get value():number{return 1;} set value(next:number|string){}} interface I extends A{} function write<T>(box:T&Readonly<I>){box.value=1;}", "TS2540"),
            ("class A{get value():number{return 1;} set value(next:number|string){}} interface I extends A{} function write<T>(box:T&Readonly<I>&{value:number}){box.value=2;}", ""),
            ("class A{get value():number{return 1;}} interface I extends A{} function write<T>(box:T&I&{value:number}){box.value=2;}", ""),
            ("class A{private value=1;} interface I extends A{} function write<T>(box:T&I){box.value=2;}", "TS2341"),
            ("class A{protected value=1;} interface I extends A{} function write<T>(box:T&I){box.value=2;}", "TS2445"),
            ("class A{protected value=1;} class B extends A{write<T>(box:T&I){box.value=2;}} interface I extends A{}", "TS2446"),
            ("class A{private value=1;write<T>(box:T&I){box.value=2;}} interface I extends A{}", ""),
            ("class A{protected value=1;} class B extends A{write<T>(box:T&I){box.value=2;}} interface I extends B{}", ""),
            ("class A{private value=1;} interface I extends A{} function write<T>(box:T&I){box['value']=2;}", ""),
            ("class A{get value():number{return 1;}} interface I extends A{} function write<T>(box:T&I){box.value=2;}", "TS2540"),
            ("class A{get value():number{return 1;} set value(next:number|string){}} interface I extends A{} function write<T>(box:T&I){box.value='ok';}", ""),
            ("class A{get value():number{return 1;} private set value(next:number){}} interface I extends A{} function write<T>(box:T&I){box.value=2;}", "TS2341"),
            ("class A{protected value=1;} class B extends A{write<T>(box:T&A&B){box.value=2;}}", ""),
            ("class A{protected value=1;} class B extends A{write<T extends B>(box:T&I){box.value=2;}} interface I extends A{}", ""),
            ("class C{private value=1;other=1;} function write<T>(box:T&C&{value:number}){box.other=2;}", "TS2339"),
            ("class C{private value=1;} function write<T>(box:T&C&{value:number}){box.value=2;}", "TS2339"),
            ("class C{private value=1;other=1;} function write<T>(box:T&C&{value:number}){box['other']=2;}", "TS2322"),
            ("class A{private value=1;other=1;} class B{private value=1;} function write<T>(box:T&A&B){box.other=2;}", "TS2339"),
            ("class A{private value=1;other=1;} class B{private value=1;} function write<T extends A>(box:T&B){box.other=2;}", "TS2339"),
            ("class A{private value=1;other=1;} interface I extends A{} function write<T>(box:T&I&{value:number}){box.other=2;}", "TS2339"),
            ("class A{private value=1;other=1;} class B{private value=1;other=1;} class C{private value=1;other=1;} function write<T extends A|B>(box:T&C){box.other=2;}", "TS2339"),
            ("class C{protected value=1;other=1;} function write<T>(box:T&C&{value:number}){box.other=2;}", ""),
            ("class A{private value=1;other=1;} class B extends A{} function write<T>(box:T&A&B){box.other=2;}", ""),
            ("class A{private value=1;other=1;} interface I extends A{} function write<T>(box:T&A&I){box.other=2;}", ""),
            ("class A{#value=1;other=1;} class B{#value=1;} function write<T>(box:T&A&B){box.other=2;}", ""),
            ("class A{private value=1;other=1;} class B{private value=1;other=1;} function write<T extends A|{other:number}>(box:T&B){box.other=2;}", ""),
            ("class A{private value=1;other=1;} class B{private value=1;other=1;} function write<T extends A|B>(box:T&B){box.other=2;}", ""),
            ("class A<U>{private value!:U;other=1;} class B extends A<number>{} function write<T>(box:T&A<number>&B){box.other=2;}", ""),
            ("type Rec<T>=T&{value:number;next:Rec<T>}; function write<T>(box:Rec<T>){box.value=2;}", ""),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void WholeIntersectionPrivateCollisionsPreserveDeclarationIdentityAndViableUnionArms(string source, string code, bool modules)
    {
        var diagnostics = Check(source, modules, exact: false);
        if (code.Length == 0) Assert.Empty(diagnostics);
        else Assert.Equal(code, Assert.Single(diagnostics).TsCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OptionalIntersectionSlotsRespectTheExactWriteOption(bool exact, bool modules)
    {
        var diagnostics = Check("function write<T>(box:T&{value?:number}){box.value=undefined;}", modules, exact);
        if (exact) Assert.Equal("TS2322", Assert.Single(diagnostics).TsCode);
        else Assert.Empty(diagnostics);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules, bool exact)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { ExactOptionalPropertyTypes = exact });
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "intersection-brands-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

