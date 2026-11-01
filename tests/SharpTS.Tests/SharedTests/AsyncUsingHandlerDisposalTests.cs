using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncUsingHandlerDisposalTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["async-try", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const log:string[]=[];try{using resource={[Symbol.dispose](){log.push(\"dispose\")}};log.push(\"body\");await 0}finally{log.push(\"finally\")}console.log(log.join(\",\"))}run();", "body,dispose,finally\n"];
        yield return ["async-catch", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const log:string[]=[];try{throw 7}catch(error){using resource={[Symbol.dispose](){log.push(\"dispose\")}};log.push(\"catch\"+error);await 0}finally{log.push(\"finally\")}console.log(log.join(\",\"))}run();", "catch7,dispose,finally\n"];
        yield return ["async-finally", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const log:string[]=[];try{log.push(\"try\")}finally{using resource={[Symbol.dispose](){log.push(\"dispose\")}};log.push(\"finally\");await 0}console.log(log.join(\",\"))}run();", "try,finally,dispose\n"];
        yield return ["async-return", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){try{using resource={[Symbol.dispose](){log.push(\"dispose\")}};await 0;return 7}finally{log.push(\"finally\")}}run().then(value=>console.log(value,log.join(\",\")));", "7 dispose,finally\n"];
        yield return ["await-using-real-disposer", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const log:string[]=[];try{await using resource={async[Symbol.asyncDispose](){log.push(\"start\");await 0;log.push(\"end\")}};log.push(\"body\");await 0}finally{log.push(\"finally\")}console.log(log.join(\",\"))}run();", "body,start,end,finally\n"];
        yield return ["interleaved-handler-resources", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(id:number){try{using first={[Symbol.dispose](){log.push(\"d\"+id+\"a\")}};await 0;using second={[Symbol.dispose](){log.push(\"d\"+id+\"b\")}};log.push(\"b\"+id)}finally{log.push(\"f\"+id)}}Promise.all([run(1),run(2)]).then(()=>console.log(log.join(\",\")));", "b1,d1b,d1a,f1,b2,d2b,d2a,f2\n"];
        yield return ["await-null-checkpoint", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){try{await using resource=null;log.push(\"body\")}finally{log.push(\"finally\")}}const pending=run();log.push(\"outside\");pending.then(()=>console.log(log.join(\",\")));", "body,outside,finally\n"];
        yield return ["await-sync-fallback-ignores-result", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];let reads=0;async function run(){try{await using resource={[Symbol.dispose](){log.push(\"dispose\");return {get then(){reads++;throw 9}}}};log.push(\"body\")}finally{log.push(\"finally\")}}const pending=run();log.push(\"outside\");pending.then(()=>console.log(log.join(\",\"),reads));", "body,dispose,outside,finally 0\n"];
        yield return ["async-disposer-rejection-identity", "/// <reference lib=\"esnext.disposable\" />\nconst marker={},log:string[]=[];async function run(){try{await using resource={async[Symbol.asyncDispose](){log.push(\"dispose\");await 0;throw marker}};await 0}catch(error){console.log(error===marker,log.join(\",\"))}}run();", "true dispose\n"];
        yield return ["async-guest-prefix-identity", "/// <reference lib=\"esnext.disposable\" />\nconst marker=\"RangeError: guest\",log:string[]=[];async function run(){try{using resource={[Symbol.dispose](){log.push(\"dispose\")}};await 0;throw marker}catch(error){console.log(error===marker,typeof error,log.join(\",\"))}}run();", "true string dispose\n"];
        yield return ["async-ordinary-block", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const log:string[]=[];{using resource={[Symbol.dispose](){log.push(\"dispose\")}};}await 0;log.push(\"after\");console.log(log.join(\",\"))}run();", "dispose,after\n"];
        yield return ["async-function-body", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){using resource={[Symbol.dispose](){log.push(\"dispose\")}};await 0;log.push(\"body\")}run().then(()=>console.log(log.join(\",\")));", "body,dispose\n"];
        yield return ["async-arrow-owner", "/// <reference lib=\"esnext.disposable\" />\nconst run=async()=>{const log:string[]=[];try{using resource={[Symbol.dispose](){log.push(\"dispose\")}};await 0;log.push(\"body\")}finally{log.push(\"finally\")}console.log(log.join(\",\"))};run();", "body,dispose,finally\n"];
        yield return ["async-forward-capture-tdz", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const log:string[]=[];{const read=()=>resource;let caught=false;try{read()}catch(error){caught=error instanceof ReferenceError}using resource={id:3,[Symbol.dispose](){log.push(\"dispose\")}};await 0;console.log(caught,read()===resource,resource.id)}console.log(log.join(\",\"))}run();", "true true 3\ndispose\n"];
        yield return ["initializer-await-and-pending-return", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){try{await using first=await Promise.resolve({async[Symbol.asyncDispose](){log.push(\"first-start\");await 0;log.push(\"first-end\")}});await using second={async[Symbol.asyncDispose](){log.push(\"second-start\");await 0;log.push(\"second-end\")}};log.push(\"body\");return 7}finally{log.push(\"finally\")}}run().then(value=>console.log(value,log.join(\",\")));", "7 body,second-start,second-end,first-start,first-end,finally\n"];
        yield return ["loop-resource-values-after-resume", "/// <reference lib=\"esnext.disposable\" />\nconst log:string[]=[];async function run(){for(let id=0;id<2;id++){using resource={id,[Symbol.dispose](){log.push(\"d\"+this.id)}};await 0;log.push(\"b\"+resource.id)}}run().then(()=>console.log(log.join(\",\")));", "b0,d0,b1,d1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpretedHandlersDisposeWithinTheirActualScope(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompiledHandlersDisposeWithinTheirActualScope(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
