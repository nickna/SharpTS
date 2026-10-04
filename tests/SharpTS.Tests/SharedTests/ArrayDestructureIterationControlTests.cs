using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayDestructureIterationControlTests
{
    // Passing controls for #1750's finite design transfer. Failing partial,
    // empty, nested and abrupt-order sources remain fixtures outside this data.
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original-rest", """
            const source:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;return {value:this.i*3,done:this.i>3};}};const [a,...rest]=source;console.log(a,rest.join(","),source.i);
            """, "3 6,9 4\n" };
        yield return new object[] { "original-generator", """
            function* values(){try{yield 2;yield 3;}finally{console.log("closed");}}const [a]=values();console.log(a);
            """, "closed\n2\n" };
        yield return new object[] { "array-rest-control", """
            const [a,...rest]=[1,2,3];console.log(a,rest.join(","));
            """, "1 2,3\n" };
        yield return new object[] { "exhausted-default-control", """
            const source:any={reads:0,closed:0,[Symbol.iterator](){return this;},next(){this.reads++;return {done:true};},return(){this.closed++;return {done:true};}};const [a=7,b=9]=source;console.log(a,b,source.reads,source.closed);
            """, "7 9 1 0\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void FullRestAndExhaustionControlsRetainBehavior(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
