using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SiblingBlockGetterCaptureTests
{
    public const string Original = "{let log='';const t:any={get x(){log+='A';return 1}};console.log(t.x,log)}{let log='';const t:any={get x(){log+='B';return 2}};console.log(t.x,log)}";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", Original, "1 A\n2 B\n"];
        yield return ["two-way", """
            { let value=1; const t={get x(){return ++value},set x(next:number){value=next}}; value=5; console.log(t.x,value);t.x=10;console.log(value); }
            { let value=20; const t={get x(){return ++value},set x(next:number){value=next}}; value=30; console.log(t.x,value);t.x=40;console.log(value); }
            """, "6 6\n10\n31 31\n40\n"];
        yield return ["escaped-independent", """
            const items:any[]=[];
            { let value=1; items.push({get x(){return ++value},set x(next:number){value=next}}); }
            { let value=10; items.push({get x(){return ++value},set x(next:number){value=next}}); }
            console.log(items[0].x,items[1].x,items[0].x);items[1].x=30;console.log(items[0].x,items[1].x);
            """, "2 11 3\n4 31\n"];
        yield return ["repeated-callable-owners", """
            function make(seed:number){
              const items:any[]=[];
              { let value=seed; items.push({get x(){return ++value}}); }
              { let value=seed*10; items.push({get x(){return ++value}}); }
              return items;
            }
            const first=make(1),second=make(2);console.log(first[0].x,first[1].x,second[0].x,second[1].x,first[0].x);
            """, "2 11 3 21 3\n"];
        yield return ["nested-arrow-relay", """
            { let value=1; const make=()=>()=>++value; const read=make(); value=5;console.log(read(),value); }
            { let value=10; const make=()=>()=>++value; const read=make(); value=20;console.log(read(),value); }
            """, "6 6\n21 21\n"];
        yield return ["unique-lift-control", """
            {let value=1;const t={get x(){return ++value}};value=5;console.log(t.x,value);}
            """, "6 6\n"];
        yield return ["distinct-primitive-storage", """
            {let value='a';const t={get x(){value+='b';return value}};console.log(t.x,value);}
            {let value=10;const t={get x(){value+=2;return value}};console.log(t.x,value);}
            """, "ab ab\n12 12\n"];
        yield return ["lexical-tdz", """
            {const t={get x(){return value}};try{console.log(t.x)}catch(e){console.log(e instanceof ReferenceError)}let value=1;console.log(t.x);}
            {const t={get x(){return value}};try{console.log(t.x)}catch(e){console.log(e instanceof ReferenceError)}let value=2;console.log(t.x);}
            """, "true\n1\ntrue\n2\n"];
        yield return ["loop-reentry", """
            const items:any[]=[];
            for(let i=0;i<2;i++){
              {let value=i;items.push({get x(){return ++value}});}
              {let value=i+10;items.push({get x(){return ++value}});}
            }
            console.log(items.map(t=>t.x).join(','));console.log(items.map(t=>t.x).join(','));
            """, "1,11,2,12\n2,12,3,13\n"];
        yield return ["sibling-for-headers", """
            const reads:(()=>number)[]=[];
            for(let value=0;value<3;value++)reads.push(()=>value);
            for(let value=10;value<13;value++)reads.push(()=>value);
            console.log(reads.map(read=>read()).join(','));
            """, "0,1,2,10,11,12\n"];
        yield return ["sibling-for-header-mutation-continue", """
            const reads:(()=>number)[]=[];
            for(let value=0;value<4;value++){if(value===1)continue;reads.push(()=>++value);}
            for(let value=10;value<13;value++){if(value===11)continue;reads.push(()=>++value);}
            console.log(reads.map(read=>read()).join(','));console.log(reads.map(read=>read()).join(','));
            """, "1,3,4,11,13\n2,4,5,12,14\n"];
        yield return ["sibling-switch-scopes", """
            switch(1){case 1:let value=1;const t={get x(){return ++value}};value=5;console.log(t.x,value);break;}
            switch(1){case 1:let value=10;const t={get x(){return ++value}};value=20;console.log(t.x,value);break;}
            """, "6 6\n21 21\n"];
        yield return ["nested-callable-owner-isolation", """
            function make(seed:number){
              let value=seed;const outer=()=>value;
              function inner(){let value=seed*10;return ()=>++value}
              const other=inner();return {outer,other,bump(){return ++value}};
            }
            const a=make(1),b=make(2);console.log(a.bump(),a.outer(),a.other(),b.outer(),b.other());
            """, "2 2 11 2 21\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void SiblingBindingsRemainLiveAndIndependent(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    public static Dictionary<string,string> ModuleFiles() => new()
    {
        ["main.ts"] = "import {items as left} from './left';import {items as right} from './right';console.log(left[0].x,left[1].x,right[0].x,right[1].x,left[0].x);",
        ["left.ts"] = "export const items:any[]=[];{let value=1;items.push({get x(){return ++value}})}{let value=10;items.push({get x(){return ++value}})}",
        ["right.ts"] = "export const items:any[]=[];{let value=100;items.push({get x(){return ++value}})}{let value=1000;items.push({get x(){return ++value}})}"
    };

    [Theory, ModeData]
    public void ActualModuleOwnersKeepRepeatedNamesSeparate(ExecutionMode mode) =>
        Assert.Equal("2 11 101 1001 3\n", TestHarness.RunModules(ModuleFiles(), "main.ts", mode));
}
