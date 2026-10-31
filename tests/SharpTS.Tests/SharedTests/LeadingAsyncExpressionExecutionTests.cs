using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LeadingAsyncExpressionExecutionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["leading-arrow", "async()=>{await 0;return Promise;};const run=async()=>{await 0;console.log(7);};run();", "7\n"];
        yield return ["contextual-identifier-call", "const async=(value:number)=>value+1;async(1);console.log(async(7));", "8\n"];
        yield return ["namespace-statement", "namespace Box {async()=>1;export const value=7;}console.log(Box.value);", "7\n"];
        yield return ["async-declaration", "async function run(){await 0;return 7;}run().then(value=>console.log(value));", "7\n"];
        yield return ["async-generator-declaration", "async function* run(){yield 7;}run().next().then(value=>console.log(value.value));", "7\n"];
    }
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], row[1], row[2], mode }));
    [Theory, MemberData(nameof(Modes))]
    public void AsyncGrammarRetainsOrdinaryExecution(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}