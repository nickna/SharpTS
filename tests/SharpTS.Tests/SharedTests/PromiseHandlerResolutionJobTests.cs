using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseHandlerResolutionJobTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-order","const events:string[]=[];const value:any={then(resolve:any){events.push(\"then\");resolve(9);}};Promise.resolve(1).then(()=>{events.push(\"handler\");return value;}).then(result=>console.log(result,events.join(\",\")));queueMicrotask(()=>events.push(\"job\"));","9 handler,job,then\n"];
        yield return ["explicit-return","const events:string[]=[];const p:any=Promise.resolve(7);Object.setPrototypeOf(p,{then(resolve:any){events.push(\"then\");resolve(9);}});Promise.resolve(1).then(()=>{events.push(\"handler\");return p;}).then(result=>console.log(result,events.join(\",\")));queueMicrotask(()=>events.push(\"job\"));","9 handler,job,then\n"];
        yield return ["getter-capture","const events:string[]=[];let gets=0,calls=0,replacements=0;const value:any={};Object.defineProperty(value,\"then\",{get(){gets++;events.push(\"get\");Object.defineProperty(value,\"then\",{value(){replacements++;}});queueMicrotask(()=>events.push(\"getter-job\"));return function(this:any,resolve:any){calls++;events.push(this===value?\"then\":\"wrong\");resolve(9);};},configurable:true});Promise.resolve(1).then(()=>{events.push(\"handler\");return value;}).then(result=>console.log(result,gets,calls,replacements,events.join(\",\")));queueMicrotask(()=>events.push(\"job\"));","9 1 1 0 handler,get,job,getter-job,then\n"];
        yield return ["getter-abrupt","const events:string[]=[];const marker={};const value:any={get then(){events.push(\"get\");throw marker;}};Promise.resolve(1).then(()=>{events.push(\"handler\");return value;}).then(()=>console.log(\"wrong\"),error=>console.log(error===marker,events.join(\",\")));queueMicrotask(()=>events.push(\"job\"));","true handler,get,job\n"];
        yield return ["callable-thenable","const events:string[]=[];const value:any=()=>{};Object.defineProperty(value,\"then\",{value(resolve:any){events.push(\"then\");resolve(9);}});Promise.resolve(1).then(()=>{events.push(\"handler\");return value;}).then(result=>console.log(result,events.join(\",\")));queueMicrotask(()=>events.push(\"job\"));","9 handler,job,then\n"];
        yield return ["primitive-result","Promise.resolve(1).then(()=>7).then(value=>console.log(value));","7\n"];
        yield return ["noncallable-then","const value:any={then:7};Promise.resolve(1).then(()=>value).then(result=>console.log(result===value));","true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReturnedThenablesRunAsTheirOwnPromiseJobs(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
