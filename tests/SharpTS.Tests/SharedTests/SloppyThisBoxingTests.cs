using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SloppyThisBoxingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["stable-and-fresh", "function read(this:any){return [this,this]}const bound=read.bind(8);const a=bound(),b=bound();console.log(a[0]===a[1],b[0]===b[1],a[0]!==b[0],a[0].valueOf());", "true true true 8\n"];
        yield return ["captured-arrow", "function make(this:any){return ()=>this}const bound=make.bind(8);const a=bound(),b=bound();console.log(typeof a(),a()===a(),a()!==b());", "object true true\n"];
        yield return ["function-expression", "const read=function(this:any){return [this,this]};const bound=read.bind(8);const a=bound(),b=bound();console.log(typeof a[0],a[0]===a[1],a[0]!==b[0],a[0].valueOf());", "object true true 8\n"];
        yield return ["default-parameter", "function read(this:any,self:any=this){console.log(self===this,typeof this)}read.bind(8)();", "true object\n"];
        yield return ["bare-strict-after-sloppy", "function sloppy(this:any){return this}function strict(this:any){'use strict';return this}console.log(typeof sloppy(),strict()===undefined);", "object true\n"];
    }

    public static IEnumerable<object[]> DualCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [.. row, mode];
    }

    [Theory, MemberData(nameof(DualCases))]
    public void SloppyCallsRetainOneBoxPerInvocation(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
