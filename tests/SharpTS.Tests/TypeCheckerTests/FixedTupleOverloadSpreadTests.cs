using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class FixedTupleOverloadSpreadTests
{
    public static IEnumerable<object[]> Valid()
    {
        yield return ["plain", "declare function pick(x:string,y:number):'pair';declare function pick(x:boolean):'boolean';const args:[string,number]=['x',1];const result:'pair'=pick(...args);"];
        yield return ["readonly", "declare function pick(x:string,y:number):'pair';declare function pick(x:boolean):'boolean';const args=['x',1] as const;const result:'pair'=pick(...args);"];
        yield return ["tuple-versus-spread", "declare function pick(x:[string,number]):'tuple';declare function pick(x:string,y:number):'pair';const args:[string,number]=['x',1];const a:'tuple'=pick(args);const b:'pair'=pick(...args);"];
        yield return ["any-subtype-pass", "declare function pick(x:number,y:number):'number';declare function pick(x:any,y:any):'any';const args:[any,any]=[1,2];const result:'any'=pick(...args);"];
        yield return ["declaration-order", "declare function pick(x:object,y:number):'broad';declare function pick(x:{tag:string},y:number):'record';const args:[{tag:string},number]=[{tag:'x'},1];const result:'broad'=pick(...args);"];
        yield return ["mixed-generic", "declare function pick<T>(x:T,y:number):T;declare function pick(x:boolean):boolean;const args:[string,number]=['x',1];const result:string=pick(...args);"];
        yield return ["generic-overloads", "declare function pick<T extends number>(x:T,y:T):T;declare function pick<T extends string>(x:T,y:T):T;const args:[number,number]=[1,2];const result:number=pick(...args);"];
        yield return ["explicit-generic", "declare function pick<T>(x:T,y:T):T;declare function pick<T>(x:T,y:T,z:T):T;const args:[number,number]=[1,2];const result:number=pick<number>(...args);"];
        yield return ["empty", "declare function pick():'empty';declare function pick(x:number):'number';const args:[]=[];const result:'empty'=pick(...args);"];
        yield return ["optional", "declare function pick(x:string,y?:number):'pair';declare function pick(x:boolean):'boolean';const args:[string,number?]=['x'];const result:'pair'=pick(...args);"];
        yield return ["optional-any", "declare function pick(x:any,y:any):'pair';declare function pick(x:boolean):'boolean';const args:[any,any?]=[1];const result:'pair'=pick(...args);"];
        yield return ["rest-array", "declare function pick(x:string,...values:number[]):'rest';declare function pick(x:boolean):'boolean';const args:[string,number,number]=['x',1,2];const result:'rest'=pick(...args);"];
        yield return ["fresh-after-spread", "type Action={type:'size';value:number};declare function pick(a:number,b:number,value:Action):'action';declare function pick(a:string,b:string,value:any):'other';const head:[number,number]=[1,2];const result:'action'=pick(...head,{type:'size',value:3});"];
        yield return ["constrained-fresh-inference", "type Action={type:'size';value:number};declare function pick<T extends Action>(a:number,b:number,value:T):T;declare function pick(a:string,b:string,value:any):any;const head:[number,number]=[1,2];const result=pick(...head,{type:'size',value:3});const kind:'size'=result.type;"];
        yield return ["callback-after-spread", "declare function pick(a:number,b:number,callback:(value:number)=>number):number;declare function pick(a:string,b:string,callback:(value:string)=>string):string;const head:[number,number]=[1,2];const result:number=pick(...head,value=>value+1);"];
        yield return ["inline-literal", "declare function pick(x:string,y:number):'pair';declare function pick(x:boolean):'boolean';const result:'pair'=pick(...['x',1]);"];
    }

    public static IEnumerable<object[]> Invalid()
    {
        yield return ["wrong-type", "declare function pick(x:string,y:number):void;declare function pick(x:boolean):void;const args:[number,number]=[1,2];pick(...args);"];
        yield return ["too-few", "declare function pick(x:string,y:number):void;declare function pick(x:boolean):void;const args:[string]=['x'];pick(...args);"];
        yield return ["too-many", "declare function pick(x:string,y:number):void;declare function pick(x:boolean):void;const args:[string,number,number]=['x',1,2];pick(...args);"];
        yield return ["optional-required", "declare function pick(x:string,y:number):void;declare function pick(x:boolean):void;const args:[string,number?]=['x'];pick(...args);"];
        yield return ["spread-is-not-tuple-value", "declare function pick(x:[number,string]):void;declare function pick(x:boolean):void;const args:[number,string]=[1,'x'];pick(...args);"];
        yield return ["retained-domain", "type Action={type:'size';value:number};declare function pick(a:number,b:number,value:Action):void;declare function pick(a:string,b:string,value:any):void;const head:[number,number]=[1,2],value={type:'size',value:3};pick(...head,value);"];
        yield return ["callback-context", "declare function pick(a:number,b:number,callback:(value:number)=>number):number;declare function pick(a:string,b:string,callback:(value:string)=>string):string;const head:[number,number]=[1,2];pick(...head,value=>value.missing);"];
        yield return ["mixed-interface-callback", "interface Choose{(head:number,callback:(x:number)=>string):string;<T extends string>(head:T,callback:(x:T)=>T):T;}declare const pick:Choose;pick(...([1,(x:number)=>x] as const));"];
    }

    public static IEnumerable<object[]> Modes() => Valid().SelectMany(row => new[] { false, true }
        .Select(modules => new object[] { row[0], row[1], modules }));

    [Theory, MemberData(nameof(Modes))]
    public void CheckedSlotsRetainArityTypesContextAndOverloadOrder(string name, string source, bool modules)
    {
        Assert.NotEmpty(name);
        Assert.Empty(Check(source, modules));
    }

    [Theory, MemberData(nameof(Invalid))]
    public void InvalidSlotTypesAndArityRemainRejected(string name, string source)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(Check(source, false));
    }

    [Fact]
    public void RealDefaultLibraryReflectOverloadsAcceptThePublishedProgram()
    {
        const string source = "const target:any={slot:7};const args:[any,any,any]=[target,'slot',8];console.log(Reflect.set(...args),target.slot);";
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fixed-overload-spread-default", "main.ts"));
        var resolver = new ModuleResolver(path, new Dictionary<string,string> { [path] = source }, TypeScriptProgramOptions.Default);
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "fixed-overload-spread", "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string,string> { [path] = source });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
