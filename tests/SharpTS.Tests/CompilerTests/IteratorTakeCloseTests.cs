using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class IteratorTakeCloseTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const source:any={i:0,closed:0,next(){this.i++;return {value:this.i,done:this.i>3};},return(){this.closed++;return {done:true};}};console.log(Iterator.from(source).take(1).toArray().join(","),source.closed);
            """, "1 1\n" };
        yield return new object[] { "original-direct", """
            const source:any={closed:0,return(){this.closed++;return {done:true};}};console.log(source.return().done,source.closed);
            """, "true 1\n" };
        yield return new object[] { "zero-limit", """
            const source:any={reads:0,closed:0,next(){this.reads++;return {value:1,done:false};},return(){this.closed++;return {done:true};}};const helper=Iterator.from(source).take(0);console.log(source.reads,source.closed);console.log(helper.next().done,source.reads,source.closed);console.log(helper.next().done,source.reads,source.closed);
            """, "0 0\ntrue 0 1\ntrue 0 1\n" };
        yield return new object[] { "close-on-resume", """
            const source:any={reads:0,closed:0,next(){this.reads++;return {value:7,done:false};},return(){this.closed++;return {done:true};}};const helper=Iterator.from(source).take(1);console.log(helper.next().value,source.reads,source.closed);console.log(helper.next().done,source.reads,source.closed);console.log(helper.next().done,source.reads,source.closed);
            """, "7 1 0\ntrue 1 1\ntrue 1 1\n" };
        yield return new object[] { "normal-exhaustion", """
            const source:any={reads:0,closed:0,next(){this.reads++;return {value:7,done:this.reads>1};},return(){this.closed++;return {done:true};}};const helper=Iterator.from(source).take(3);console.log(helper.toArray().join(","),source.reads,source.closed);console.log(helper.next().done,source.reads,source.closed);
            """, "7 2 0\ntrue 2 0\n" };
        yield return new object[] { "generator-finally", """
            function* values(){try{yield 2;yield 3;}finally{console.log("closed");}}const helper=Iterator.from(values()).take(1);console.log(helper.toArray().join(","));console.log(helper.next().done);
            """, "closed\n2\ntrue\n" };
        yield return new object[] { "next-throws", """
            const marker={tag:7};const source:any={reads:0,closed:0,next(){this.reads++;throw marker;},return(){this.closed++;return {done:true};}};const helper=Iterator.from(source).take(1);try{helper.next();}catch(e:any){console.log(e===marker,source.reads,source.closed);}console.log(helper.next().done,source.reads,source.closed);
            """, "true 1 0\ntrue 1 0\n" };
        yield return new object[] { "return-throws", """
            const marker={tag:7};const source:any={closed:0,next(){return {value:7,done:false};},return(){this.closed++;throw marker;}};const helper=Iterator.from(source).take(1);try{helper.toArray();}catch(e:any){console.log(e===marker,source.closed);}console.log(helper.next().done,source.closed);
            """, "true 1\ntrue 1\n" };
        yield return new object[] { "return-primitive", """
            const source:any={closed:0,next(){return {value:7,done:false};},return(){this.closed++;return Symbol("close");}};const helper=Iterator.from(source).take(1);try{helper.toArray();}catch(e:any){console.log(e.name,source.closed);}console.log(helper.next().done,source.closed);
            """, "TypeError 1\ntrue 1\n" };
        yield return new object[] { "lazy-pipeline", """
            const source:any={reads:0,closed:0,next(){this.reads++;return {value:this.reads,done:false};},return(){this.closed++;return {done:true};}};const helper=Iterator.from(source).drop(1).map((n:any)=>n*2).filter((n:any)=>n>2).take(1);console.log(helper.toArray().join(","),source.reads,source.closed);
            """, "4 2 1\n" };
        yield return new object[] { "flatmap-pipeline", """
            function* outer(){try{yield 1;yield 2;}finally{console.log("outer");}}function* inner(n:number){try{yield n*3;yield n*4;}finally{console.log("inner");}}console.log(Iterator.from(outer()).flatMap((n:number)=>inner(n)).take(1).toArray().join(","));
            """, "inner\nouter\n3\n" };
        yield return new object[] { "flatmap-close-throws", """
            const marker={tag:7};function* outer(){try{yield 1;}finally{console.log("outer");throw {tag:9};}}function* inner(){try{yield 3;yield 4;}finally{console.log("inner");throw marker;}}try{Iterator.from(outer()).flatMap(()=>inner()).take(1).toArray();}catch(e:any){console.log(e===marker);}
            """, "inner\nouter\ntrue\n" };
        yield return new object[] { "closed-pipeline-stays-done", """
            const mapped=Iterator.from([1,2,3]).map((n:any)=>n*2);console.log(mapped.take(1).toArray().join(","),mapped.next().done);
            const filtered=Iterator.from([1,2,3]).filter((n:any)=>n>0);console.log(filtered.take(1).toArray().join(","),filtered.next().done);
            const dropped=Iterator.from([1,2,3]).drop(1);console.log(dropped.take(1).toArray().join(","),dropped.next().done);
            const flattened=Iterator.from([1,2]).flatMap((n:any)=>[n,n+1]);console.log(flattened.take(1).toArray().join(","),flattened.next().done);
            """, "2 true\n1 true\n2 true\n1 true\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledTakeClosesOnLimitAndPreservesExhaustionAndThrows(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
