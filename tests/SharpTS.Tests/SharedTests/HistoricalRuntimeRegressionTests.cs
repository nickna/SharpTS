using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class HistoricalRuntimeRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue1947-original.ts", "const proto:any={set value(v:number){(this as any).own=v;}};const value:any=Object.create(proto);value.value=9;console.log(value.own,Object.hasOwn(value,'own'),Object.hasOwn(proto,'own'));", "9 true false\n"];
        yield return ["issue1947-controls.ts", """
            const proto:any={get value(){return (this as any).own;},set value(v:number){(this as any).own=v;}};const middle:any=Object.create(proto);const value:any=Object.create(middle);value['value']=6;console.log(value.value,Object.hasOwn(value,'value'),Object.hasOwn(proto,'own'));
            Object.defineProperty(value,'value',{value:20,writable:true});value.value=21;console.log(value.value,value.own);
            const defined:any={};Object.defineProperty(defined,'value',{set(v:number){(this as any).own=v;}});const child:any=Object.create(defined);child['value']=8;console.log(child.own,Object.hasOwn(defined,'own'));
            """, "6 false false\n21 6\n8 false\n"];
        yield return ["issue1946-original.ts", "const proto:any={base:3,get value(){return (this as any).own+this.base;}};const value:any=Object.create(proto);value.own=4;console.log(value.value,'base' in value,Object.hasOwn(value,'base'),Object.getPrototypeOf(value)===proto);", "7 true false true\n"];
        yield return ["issue1946-controls.ts", """
            let calls=0;const proto:any={base:3,get value(){calls++;return (this as any).own+this.base;}};const middle:any=Object.create(proto);const value:any=Object.create(middle);value.own=4;console.log(value['value'],calls,Object.hasOwn(proto,'own'));
            Object.defineProperty(value,'value',{value:20});console.log(value.value,calls);
            const defined:any={};Object.defineProperty(defined,'value',{get(){return (this as any).own;}});const child:any=Object.create(defined);child.own=8;console.log(child.value);
            """, "7 1 false\n20 1\n8\n"];
        yield return ["issue1945-original.cjs", "const N=Number;console.log(Number('2'),N('2'),N===Number);", "2 2 true\n"];
        yield return ["issue1945-controls.cjs", "const N=Number;console.log(N(),N(null),N(true),N('0x10'),N(42n),Number.isNaN(N(undefined)));const value={valueOf(){return 7;}};console.log(N(value),N===Number);", "0 0 1 16 42 true\n7 true\n"];
        yield return ["issue1944-original.cjs", "const B=Boolean;console.log(Boolean(0),Boolean('x'),B(0),B('x'),B===Boolean);", "false true false true true\n"];
        yield return ["issue1944-controls.cjs", "const B=Boolean;console.log(B(),B(undefined),B(null),B(false),B(0),B(''),B('false'),B({}));", "false false false false false false true true\n"];
        yield return ["issue1942-original.cjs", "const B=BigInt;console.log(BigInt('123'),B('123'),B.asUintN(8,-1n),(255n).toString(16));", "123n 123n 255n ff\n"];
        yield return ["issue1942-controls.cjs", "const B=BigInt;console.log(B===BigInt,B(42),B(true),B('0xff'));try{B(1.5);}catch(e){console.log(e.name);}console.log(B.asIntN(8,255n));", "true 42n 1n 255n\nRangeError\n-1n\n"];
        yield return ["issue1941-original.ts", """
            const buffer=new ArrayBuffer(16);const view=new DataView(buffer);view.setBigInt64(0,-1n,true);view.setBigInt64(8,123n,true);const a=new BigInt64Array(buffer);const b=new BigUint64Array(buffer);console.log(a[0],a[1],a.length,b[0],b[1]);const value:any=a;try{value[0]=1n;}catch(e:any){console.log('assignment failed');}
            """, "-1n 123n 2 18446744073709551615n 123n\n"];
        yield return ["issue1941-controls.ts", """
            const buffer=new ArrayBuffer(8);const signed:any=new BigInt64Array(buffer);const unsigned:any=new BigUint64Array(buffer);
            signed[0]=-1n;console.log(signed[0],unsigned[0]);
            unsigned[0]=18446744073709551617n;console.log(signed[0],unsigned[0]);
            signed[0]=9223372036854775808n;console.log(signed[0],unsigned[0]);
            for(const value of [1,null,undefined]){try{signed[0]=value;}catch(e:any){console.log(e.name);}}
            console.log(signed[0],unsigned[0]);
            """, "-1n 18446744073709551615n\n1n 1n\n-9223372036854775808n 9223372036854775808n\nTypeError\nTypeError\nTypeError\n-9223372036854775808n 9223372036854775808n\n"];
    }

    public static IEnumerable<object[]> ApiCases()
    {
        foreach (var item in Cases())
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
                yield return [..item, mode];
    }

    [Theory, MemberData(nameof(ApiCases))]
    public void PreservedSourcesMatchReference(string file, string source, string expected, ExecutionMode mode)
    {
        string actual = file.EndsWith(".cjs", StringComparison.Ordinal)
            ? TestHarness.RunModules(new() { [file] = source }, file, mode)
            : TestHarness.Run(source, mode);
        Assert.Equal(expected, actual);
    }
}
