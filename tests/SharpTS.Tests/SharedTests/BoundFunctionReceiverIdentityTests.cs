using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BoundFunctionReceiverIdentityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["identity", "const target:any={value:8};function read(this:any){console.log(this===target,this.value)}read.bind(target)();", "true 8\n"];
        yield return ["live-fields", "const target:any={value:8};function read(this:any){console.log(this===target,this.value)}const bound=read.bind(target);target.value=9;bound();", "true 9\n"];
        yield return ["accessor", "const target:any={};let gets=0;Object.defineProperty(target,'value',{get(){gets++;console.log(this===target);return 7}});function read(this:any){console.log(this===target,this.value)}const bound=read.bind(target);console.log(gets);bound();console.log(gets);", "0\ntrue\ntrue 7\n1\n"];
        yield return ["mutation-visible-in-body", "const target:any={value:8};function write(this:any){this.value++;console.log(target.value,this===target)}write.bind(target)();console.log(target.value);", "9 true\n9\n"];
        yield return ["mutation-before-throw", "const target:any={value:8},marker={};function write(this:any){this.value++;throw marker}try{write.bind(target)()}catch(e){console.log(e===marker,target.value)}", "true 9\n"];
        yield return ["chained", "const first:any={},second:any={};function read(this:any){console.log(this===first,this===second)}read.bind(first).bind(second)();", "true false\n"];
        yield return ["argument-prefix", "const target:any={value:8};function read(this:any,a:number,b:number){console.log(this===target,this.value,a,b)}read.bind(target,1)(2);", "true 8 1 2\n"];
        yield return ["sloppy-primitive", "function read(this:any){console.log(typeof this,this.valueOf())}read.bind(8)();", "object 8\n"];
        yield return ["strict-primitive", "function read(this:any){'use strict';console.log(typeof this,this===8)}read.bind(8)();", "number true\n"];
        yield return ["fresh-sloppy-box", "function read(this:any){return this}const bound=read.bind(8);console.log(bound()!==bound());", "true\n"];
    }

    public static IEnumerable<object[]> DualCases()
    {
        foreach (var row in Cases())
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            yield return [.. row, mode];
    }

    [Theory, MemberData(nameof(DualCases))]
    public void BoundFunctionsUseTheirOriginalReceiver(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
