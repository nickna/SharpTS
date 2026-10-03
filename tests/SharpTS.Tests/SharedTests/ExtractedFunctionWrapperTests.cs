using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExtractedFunctionWrapperTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            function f(this:any,a:number,b:number){return this.x+a+b;}function run(){const c:any=f.call;const a:any=f.apply;console.log(c.call(f,{x:1},2,3),a.call(f,{x:4},[5,6]));}run();
            """, "6 15\n" };
        yield return new object[] { "combinations", """
            function f(this:any,a:number,b:number){return this.x+a+b;}
            function g(this:any,a:number,b:number){return this.x*10+a+b;}
            const c:any=f.call; const a:any=f.apply; const b:any=f.bind;
            console.log(c.apply(f,[{x:2},3,4]),a.apply(f,[{x:5},[6,7]]));
            console.log(c.call(g,{x:1},2,3),a.call(g,{x:4},[5,6]));
            const boundCall:any=c.bind(f,{x:3},4);
            console.log(boundCall(5),boundCall.call(g,6));
            const boundApply:any=a.bind(g,{x:2},[3,4]);
            console.log(boundApply());
            console.log(b.call(g,{x:4},5)(6));
            console.log(f.bind({x:7},8).bind({x:99},9)());
            """, "9 18\n15 51\n12 13\n27\n51\n24\n" };
        yield return new object[] { "errors-and-this", """
            const token:any={tag:'same'};
            function boom(){throw token;}
            const c:any=boom.call; const a:any=boom.apply; const b:any=boom.bind;
            try{c.call(boom,null);}catch(error){console.log(error===token);}
            try{a.apply(boom,[null,[]]);}catch(error){console.log(error===token);}
            try{b.call(boom,null)();}catch(error){console.log(error===token);}
            function strict(this:any){'use strict';return this;}
            const sc:any=strict.call; const sa:any=strict.apply;
            console.log(sc.call(strict,null)===null,sa.call(strict,undefined,[])===undefined);
            """, "true\ntrue\ntrue\ntrue true\n" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ExtractedWrappersPreserveTargetReceiverArgumentsAndErrors(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
