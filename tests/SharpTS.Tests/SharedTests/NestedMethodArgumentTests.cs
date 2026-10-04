using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedMethodArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            let saved:any;const o:any={f:function(a:any,b:any){if(saved===undefined)saved=arguments;return a*10+b;}};console.log(o.f(1,2),o.f(3,4),saved[0],saved[1]);console.log(o.f(o.f(1,2),o.f(3,4)));console.log(saved[0],saved[1]);
            """, "12 34 1 2\n154\n1 2\n" };
        yield return new object[] { "materialized-original-control", """
            const o:any={f:function(a:any,b:any){return a*10+b;}};const left=o.f(1,2);const right=o.f(3,4);console.log(o.f(left,right));
            """, "154\n" };
        yield return new object[] { "spread-original-control", """
            const o:any={f:function(a:any,b:any){return a*10+b;}};console.log(o.f(...[o.f(1,2),o.f(3,4)]));
            """, "154\n" };
        yield return new object[] { "function-value-original-control", """
            const o:any={f:function(a:any,b:any){return a*10+b;}};const f:any=o.f;console.log(f(f(1,2),f(3,4)));
            """, "154\n" };
        yield return new object[] { "arity-and-arguments", """
            const retained:any[]=[];
            const o:any={
                f:function(a:any,b:any){retained.push(arguments);return a*10+b;},
                one:function(a:any){return a;},
                three:function(a:any,b:any,c:any){return a*100+b*10+c;}
            };
            console.log(o.f(o.f(1,2),o.f(3,4)));
            console.log(retained[0][0],retained[0][1],retained[1][0],retained[1][1],retained[2][0],retained[2][1]);
            console.log(retained[0]!==retained[1],retained[1]!==retained[2]);
            console.log(o.f(o.one(12),o.three(0,3,4)));
            console.log(o.f(o.f(o.f(1,2),o.f(3,4)),o.f(5,6)));
            """, "154\n1 2 3 4 12 34\ntrue true\n154\n1596\n" };
        yield return new object[] { "evaluation-and-errors", """
            const events:any[]=[];
            const o:any={f:function(a:any,b:any){events.push('invoke');return a*10+b;}};
            const target:any={get f(){events.push('get');return o.f;}};
            function receiver(){events.push('receiver');return target;}
            function left(){events.push('left');return o.f(1,2);}
            const value:any={get right(){events.push('right');return o.f(3,4);}};
            console.log(receiver().f(left(),value.right));
            console.log(events.join(','));
            const token:any={failure:true};
            function fail(){events.push('throw');throw token;}
            events.length=0;
            try{receiver().f(left(),fail());}catch(error){console.log(error===token);}
            console.log(events.join(','));
            console.log(o.f(5,6));
            """, "154\nreceiver,get,left,invoke,right,invoke,invoke\ntrue\nreceiver,get,left,invoke,throw\n56\n" };
        yield return new object[] { "indirect-callback", """
            const o:any={f:function(a:any,b:any){return a*10+b;}};
            console.log(o.f(o.f(1,2),Array.from([0],()=>o.f(3,4))[0]));
            """, "154\n" };
        yield return new object[] { "decoder-nested-control", """
            const d:any=new TextDecoder();
            const other:any=new TextDecoder();
            const decode:any=d.decode;
            const bytes:any=new Uint8Array([88,65,66,89]);
            console.log(decode.call(other,bytes.subarray(1,3)));
            console.log(decode.apply(other,[bytes.subarray(2,3)]));
            const storage:any=new ArrayBuffer(4);
            const view:any=new Uint8Array(storage);
            view[0]=88;view[1]=65;view[2]=66;view[3]=89;
            const words:any=new Uint16Array(storage,0,2);
            console.log(decode.call(other,words));
            console.log(decode.call(other).length,decode.call(other,new Uint8Array(0)).length);
            for(const receiver of [null,undefined,{},[]]){
                try{decode.call(receiver,bytes);console.log(false);}catch(error){console.log(error instanceof TypeError);}
            }
            """, "AB\nB\nXABY\n0 0\ntrue\ntrue\ntrue\ntrue\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    public static IEnumerable<object[]> CompiledCases()
    {
        foreach (var row in Cases()) yield return row;
        yield return new object[] { "coercion-indirect-control", """
            const o:any={f:function(a:any,b:any){return a*10+b;}};
            const value:any={valueOf(){return o.f(3,4);}};
            console.log(o.f(o.f(1,2),+value));
            """, "154\n" };
    }

    [Fact]
    public void CompiledArgumentCoercionCanEnterGuestCodeWithoutOverwritingEarlierArguments()
    {
        var row = CompiledCases().Last();
        Assert.Equal((string)row[2], TestHarness.Run((string)row[1], ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(ParityCases))]
    public void NestedMethodCallsPreserveArgumentLifetimeAndEvaluationOrder(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
