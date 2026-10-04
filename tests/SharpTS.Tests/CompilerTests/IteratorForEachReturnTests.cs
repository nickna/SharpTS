using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class IteratorForEachReturnTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const seen:any[]=[];const result=Iterator.from([4,5]).forEach((x:any,i:any)=>seen.push(x+i));console.log(seen.join(","),result===undefined);
            """, "4,6 true\n" };
        yield return new object[] { "original-return", """
            const r:any=Iterator.from([1]).forEach((x:any)=>x);console.log(r===null,r===undefined);
            """, "false true\n" };
        yield return new object[] { "original-values", """
            const seen:any[]=[];Iterator.from([4,5]).forEach((x:any,i:any)=>seen.push(x+i));console.log(seen.join(","));
            """, "4,6\n" };
        yield return new object[] { "empty", """
            let calls=0;const r:any=Iterator.from([]).forEach(()=>{calls++;});console.log(r===undefined,r===null,calls);
            """, "true false 0\n" };
        yield return new object[] { "generator-indices", """
            function* values(){try{yield 2;yield 5;}finally{console.log("exhausted");}}const seen:any[]=[];const r:any=Iterator.from(values()).forEach((n:any,i:any)=>{seen.push(n+i);});console.log(seen.join(","),r===undefined);
            """, "exhausted\n2,6 true\n" };
        yield return new object[] { "callback-throws", """
            const marker={tag:7};let calls=0;try{Iterator.from([2,3,4]).forEach((n:any,i:any)=>{calls++;console.log(n,i);throw marker;});console.log("accepted");}catch(e:any){console.log(e===marker,calls);}
            """, "2 0\ntrue 1\n" };
    }

    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "empty-value-callback-independent", """
            let calls=0;const r:any=Iterator.from([]).forEach(()=>{calls++;return 9;});console.log(r===undefined,r===null,calls);
            """, "true false 0\n" };
        yield return new object[] { "generator-value-callback-independent", """
            function* values(){try{yield 2;yield 5;}finally{console.log("exhausted");}}const seen:any[]=[];const r:any=Iterator.from(values()).forEach((n:any,i:any)=>{seen.push(n+i);return null;});console.log(seen.join(","),r===undefined);
            """, "exhausted\n2,6 true\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledForEachReturnsUndefinedAndPreservesCallbacks(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
