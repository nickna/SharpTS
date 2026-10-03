using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class IteratorLimitTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            console.log(Iterator.from([1,2,3,4]).drop(1).take(2).toArray().join(","));try{Iterator.from([1]).take(-1);console.log("accepted");}catch(e){console.log(e.name);}
            """, "2,3\nRangeError\n" };
        yield return new object[] { "original-zero", """
            console.log(Iterator.from([1,2,3]).take(0).toArray().length,Iterator.from([1,2,3]).drop(0).toArray().join(","));
            """, "0 1,2,3\n" };
        yield return new object[] { "negative-limits", """
            for(const limit of [-1,-1.9,-Infinity]){try{Iterator.from([1]).take(limit);console.log("accepted");}catch(e:any){console.log(e.name);}try{Iterator.from([1]).drop(limit);console.log("accepted");}catch(e:any){console.log(e.name);}}
            """, "RangeError\nRangeError\nRangeError\nRangeError\nRangeError\nRangeError\n" };
        yield return new object[] { "fractional-and-negative-zero", """
            console.log(Iterator.from([1,2,3]).take(1.9).toArray().join(","),Iterator.from([1,2,3]).drop(1.9).toArray().join(","));console.log(Iterator.from([1,2,3]).take(-0.5).toArray().length,Iterator.from([1,2,3]).drop(-0.5).toArray().join(","));console.log(Iterator.from([1]).take(-0).toArray().length,Iterator.from([1]).drop(-0).toArray().join(","));
            """, "1 2,3\n0 1,2,3\n0 1\n" };
        yield return new object[] { "nan-and-undefined", """
            for(const limit of [NaN,undefined] as any[]){try{Iterator.from([1]).take(limit);console.log("accepted");}catch(e:any){console.log(e.name);}try{Iterator.from([1]).drop(limit);console.log("accepted");}catch(e:any){console.log(e.name);}}
            """, "RangeError\nRangeError\nRangeError\nRangeError\n" };
        yield return new object[] { "large-and-infinite", """
            for(const limit of [2147483648,4294967296,Number.MAX_VALUE,Infinity]){console.log(Iterator.from([1,2,3]).take(limit).toArray().join(","),Iterator.from([1,2,3]).drop(limit).toArray().length);}
            """, "1,2,3 0\n1,2,3 0\n1,2,3 0\n1,2,3 0\n" };
        yield return new object[] { "primitive-coercion", """
            for(const limit of ["2",true,null] as any[]){console.log(Iterator.from([1,2,3]).take(limit).toArray().join(","),Iterator.from([1,2,3]).drop(limit).toArray().join(","));}
            """, "1,2 3\n1 2,3\n 1,2,3\n" };
        yield return new object[] { "object-coercion-once", """
            let calls=0;const limit:any={valueOf(){calls++;return 1.9;}};const a=Iterator.from([1,2,3]).take(limit);console.log(calls,a.toArray().join(","),calls);const b=Iterator.from([1,2,3]).drop(limit);console.log(calls,b.toArray().join(","),calls);
            """, "1 1 1\n2 2,3 2\n" };
        yield return new object[] { "invalid-closes", """
            const source:any={reads:0,closed:0,next(){this.reads++;return {done:true};},return(){this.closed++;return {done:true};}};try{Iterator.from(source).take(-1);}catch(e:any){console.log(e.name,source.reads,source.closed);}try{Iterator.from(source).drop(NaN);}catch(e:any){console.log(e.name,source.reads,source.closed);}
            """, "RangeError 0 1\nRangeError 0 2\n" };
        yield return new object[] { "invalid-close-fails", """
            const source:any={closed:0,next(){return {done:true};},return(){this.closed++;return 1n;}};try{Iterator.from(source).take(-1);}catch(e:any){console.log(e.name,source.closed);}try{Iterator.from(source).drop(NaN);}catch(e:any){console.log(e.name,source.closed);}
            """, "RangeError 1\nRangeError 2\n" };
        yield return new object[] { "coercion-throw-closes", """
            const marker={tag:7};const limit:any={valueOf(){throw marker;}};const source:any={closed:0,next(){return {done:true};},return(){this.closed++;throw {tag:9};}};try{Iterator.from(source).take(limit);}catch(e:any){console.log(e===marker,source.closed);}try{Iterator.from(source).drop(limit);}catch(e:any){console.log(e===marker,source.closed);}
            """, "true 1\ntrue 2\n" };
        yield return new object[] { "bigint-symbol-limits", """
            for(const limit of [1n,Symbol("limit")] as any[]){try{Iterator.from([1]).take(limit);}catch(e:any){console.log(e.name);}try{Iterator.from([1]).drop(limit);}catch(e:any){console.log(e.name);}}
            """, "TypeError\nTypeError\nTypeError\nTypeError\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledLimitsValidateBeforeIterationWithoutInt32Narrowing(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
