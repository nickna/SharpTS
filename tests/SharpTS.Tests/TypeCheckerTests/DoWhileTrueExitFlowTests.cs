using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DoWhileTrueExitFlowTests
{
    public static IEnumerable<object[]> ValidPrograms()
    {
        foreach (string source in new[]
        {
            "declare const choose:boolean;let value:string|number='start';do{if(choose){value='done';break;}value=1;}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';outer:do{if(choose){value='done';break outer;}value=1;}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number|boolean='start';do{const domain:string|number=value;if(choose){value='done';break;}value=1;}while(true);const good:string=value;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=+source;break;}source=1;target=1;}while(true);const good:number=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=source+'';break;}source=1;target=1;}while(true);const good:string|number=target;",
            "function identity<T>(value:T):T{return value;}declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=identity(source);break;}source=1;target=1;}while(true);const good:string|number=target;",
            "interface Reader{<T>(value:T):T;}declare const reader:Reader;declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=reader(source);break;}source=1;target=1;}while(true);const good:string|number=target;",
            "declare const reader:{<T>(value:T):T;tag:string};declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=reader(source);break;}source=1;target=1;}while(true);const good:string|number=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|boolean='s';do{if(choose){target=source===source;break;}source=1;target=false;}while(true);const good:boolean=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=source||1;break;}source=1;target=1;}while(true);const good:string|number=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=source&&1;break;}source=1;target=1;}while(true);const good:string|number=target;",
            "declare const choose:boolean;let source:string|number|null='s';let target:string|number|boolean='s';do{if(choose){target=source??1;break;}source=1;target=1;}while(true);const good:string|number=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=choose?source:1;break;}source=1;target=1;}while(true);const good:string|number=target;",
            "class Box{value:string|number;constructor(value:string|number){this.value=value;}}declare const choose:boolean;let source:string|number='s';let target:Box|boolean=new Box('s');do{if(choose){target=new Box(source);break;}source=1;target=new Box(1);}while(true);const good:Box=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number|boolean='s';do{if(choose){target=typeof source==='number'?source:1;break;}source=1;target=1;}while(true);const good:number=target;",
            "declare const choose:boolean;let source:string|number='s';let result:string|number='s';do{do{if(choose){result='done';break;}source=1;result=1;}while(true);break;}while(true);const good:string=result;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){const result='done';value=result;break;}value=1;}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){(()=>{value='done';})();break;}value=1;}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){((result:string)=>{value=result;})('done');break;}value=1;}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose)break;{let value=1;value=2;}}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(typeof value==='string')break;value='done';}while(true);const good:string=value;",
            "let value:string|number='s';do{if(typeof value==='string')break;value=1;}while(true);const good:string=value;",
            "let value:string|number='s';let result:string|number='s';do{if(typeof value==='string'){result=value;break;}value=1;}while(true);const good:string=result;",
            "let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}value=1;}while(true);const good:number=value;",
            "let value:string|number='s';let result:string|number='s';do{if(typeof value==='number'){result=value;break;}value=1;}while(true);const good:number=result;",
            "type State={kind:'text',text:string}|{kind:'count',count:number};let state:State={kind:'text',text:'a'};do{if(state.kind==='count'){break;}state={kind:'count',count:1};}while(true);const good:number=state.count;",
            "type State={kind:'text',text:string}|{kind:'count',count:number};let state:State={kind:'text',text:'a'};let result:State=state;do{if(state.kind==='count'){result=state;break;}state={kind:'count',count:1};}while(true);const good:number=result.count;",
            "declare const choose:boolean;let value:string|number='s';do{try{if(choose){value='done';break;}value=1;}catch{value='caught';break;}}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){value='done';break;}value=1;}while(false);const good:string|number=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){value='done';break;}value=1;continue;}while(true);const good:string=value;",
            "declare const choose:boolean;let value:string|number='start';declare function result():string;do{if(choose){value=result();break;}value=1;}while(true);const good:string=value;"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidPrograms))]
    public void BreaksRetainTheirCheckedAssignmentDomain(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    public static IEnumerable<object[]> InvalidPrograms()
    {
        foreach (string source in new[]
        {
            "declare const choose:boolean;let value:string|number='start';do{if(choose)break;value=1;}while(true);const wrong:string=value;",
            "declare const choose:boolean;let value:string|number='start';let source:string|number='start';do{if(choose){value=source;break;}source=1;}while(true);const wrong:string=value;",
            "function identity<T>(value:T):T{return value;}declare const choose:boolean;let source:string|number='s';let target:string|number='s';do{if(choose){target=identity(source);break;}source=1;}while(true);const wrong:string=target;",
            "interface Reader{<T>(value:T):T;}declare const reader:Reader;declare const choose:boolean;let source:string|number='s';let target:string|number='s';do{if(choose){target=reader(source);break;}source=1;target=1;}while(true);const wrong:string=target;",
            "declare const reader:{<T>(value:T):T;tag:string};declare const choose:boolean;let source:string|number='s';let target:string|number='s';do{if(choose){target=reader(source);break;}source=1;target=1;}while(true);const wrong:string=target;",
            "declare const choose:boolean;let source:string|number='s';let target:string|number='s';do{let saved=source;if(choose){target=typeof source==='string'?saved:'fallback';break;}source=1;target=1;}while(true);const wrong:string=target;",
            "declare const choose:boolean;let source:string|number='s';let result:string|number='s';do{do{if(choose){result=source;break;}source=1;result=1;}while(true);break;}while(true);const wrong:string=result;",
            "declare const choose:boolean;let value:string|number='start';let first:string|number='start';let second:string|number='start';do{if(choose){value=first;break;}first=second;second=1;}while(true);const wrong:string=value;",
            "declare const choose:boolean;let value:string|number='start';let source:string|number='start';do{if(choose){(()=>{value=source;})();break;}source=1;}while(true);const wrong:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){value='done';break;}value=1;}while(1);const wrong:string=value;",
            "declare const choose:boolean;let flag:boolean=true;let value:string|number='start';do{if(choose){value='done';break;}value=1;flag=false;}while(flag);const wrong:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){value='done';break;}value=1;}while((true));const wrong:string=value;",
            "declare const choose:boolean;let value:string|number='start';do{if(choose){value='done';break;}value=1;}while(false);const wrong:string=value;",
            "declare const choose:boolean;type State={kind:'text',text:string}|{kind:'count',count:number};let state:State={kind:'text',text:'a'};do{if(choose)break;state={kind:'count',count:1};}while(true);const wrong:{kind:'text',text:string}=state;",
            "declare const choose:boolean;let value:string|number='s';do{try{value=1;if(choose)throw 0;value='done';break;}catch{break;}}while(true);const wrong:string=value;"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(InvalidPrograms))]
    public void BackedgesCopiesAndContinuingConditionsRemainConservative(string source, bool modules)
        => Assert.Equal("TS2322", Assert.Single(Check(source, modules)).TsCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnImpossibleGuardDoesNotAuthorizeAMemberOfAnUnreachableConstituent(bool modules)
        => Assert.Equal("TS2339", Assert.Single(Check("""
            type State={kind:'text',text:string}|{kind:'count',count:number};
            let state:State={kind:'text',text:'a'};
            do {
                if(state.kind==='count'){state.count.toFixed();break;}
                state={kind:'text',text:'b'};
            } while(true);
            """, modules)).TsCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentBreakWritesRetainTheCanonicalBackedgeBodyDiagnostic(bool modules)
        => Assert.Equal("TS2339", Assert.Single(Check("""
            declare const choose:boolean;
            let source:string|number='s';let result:string|number='s';
            do {
                if(choose){const bad:number=source.length;result='done';break;}
                source=1;result=1;
            } while(true);
            const good:string=result;
            """, modules)).TsCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericReturnedRecordsKeepTheirReadonlyMemberContract(bool modules)
        => Assert.Equal("TS2540", Assert.Single(Check("""
            declare const choose:boolean;
            function make<T>(value:T):{readonly value:T}{return{value};}
            let source:string|number='s';
            let result:{readonly value:string|number}|boolean={value:source};
            do {
                if(choose){result=make(source);break;}
                source=1;result={value:source};
            } while(true);
            result.value=2;
            """, modules)).TsCode);

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "do-while-true-exit-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
