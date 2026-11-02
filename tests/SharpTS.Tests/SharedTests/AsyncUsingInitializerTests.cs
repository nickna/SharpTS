using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncUsingInitializerTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["initializer-await-and-pending-return", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){try{await using first=await Promise.resolve({async[Symbol.asyncDispose](){log.push(\"first-start\");await 0;log.push(\"first-end\")}});await using second={async[Symbol.asyncDispose](){log.push(\"second-start\");await 0;log.push(\"second-end\")}};log.push(\"body\");return 7}finally{log.push(\"finally\")}}run().then(value=>console.log(value,log.join(\",\")));", "7 body,second-start,second-end,first-start,first-end,finally\n"];
        yield return ["ordinary-using-awaited-initializer", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function make(){return {[Symbol.dispose](){log.push(\"dispose\")}}}async function run(){using resource=await make();log.push(\"body\");await 0;log.push(\"after\")}run().then(()=>console.log(log.join(\",\")));", "body,after,dispose\n"];
        yield return ["later-initializer-rejection", "/// <reference lib=\"esnext.disposable\" />\nconst marker={},log:string[]=[];async function run(){try{await using first={async[Symbol.asyncDispose](){log.push(\"dispose\");await 0;log.push(\"done\")}},second=await Promise.reject(marker)}catch(error){console.log(error===marker,log.join(\",\"))}}run();", "true dispose,done\n"];
        yield return ["interleaved-awaited-initializers", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(label:string){await using resource=await Promise.resolve({async[Symbol.asyncDispose](){log.push(\"dispose\"+label);await 0;log.push(\"done\"+label)}});log.push(\"body\"+label);await 0}Promise.all([run(\"a\"),run(\"b\")]).then(()=>console.log(log.join(\",\")));", "bodya,bodyb,disposea,disposeb,donea,doneb\n"];
        yield return ["awaited-null-initializer", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){await using resource=await Promise.resolve(null);log.push(\"body\");return 7}run().then(value=>console.log(value,log.join(\",\")));", "7 body\n"];
        yield return ["acquisition-after-initializer-before-binding", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){const resource:any={};Object.defineProperty(resource,Symbol.asyncDispose,{get(){log.push(\"get\");return function(this:any){log.push(this===resource?\"receiver\":\"wrong\")}}});await using bound=await Promise.resolve(resource);log.push(bound===resource?\"body\":\"wrong\")}run().then(()=>console.log(log.join(\",\")));", "get,body,receiver\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterAwaitsInitializersBeforeAcquisition(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompiledAwaitsInitializersBeforeAcquisition(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
