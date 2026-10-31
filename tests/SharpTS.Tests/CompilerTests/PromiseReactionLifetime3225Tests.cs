using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PromiseReactionLifetime3225Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-pending-then", "const value:any=new Promise(()=>{});value.then(()=>console.log('unreachable'));console.log('done');", "done\n"];
        yield return ["pending-catch", "const value:any=new Promise(()=>{});value.catch(()=>console.log('unreachable'));console.log('done');", "done\n"];
        yield return ["pending-finally", "const value:any=new Promise(()=>{});value.finally(()=>console.log('unreachable'));console.log('done');", "done\n"];
        yield return ["reaction-returns-pending", "Promise.resolve(7).then(()=>new Promise(()=>{}));console.log('done');", "done\n"];
        yield return ["settled-reaction-fifo", "Promise.resolve(7).then(value=>console.log(value));console.log('done');", "done\n7\n"];
        yield return ["referenced-timer", "new Promise(resolve=>setTimeout(()=>resolve(7),20)).then(value=>console.log(value));console.log('done');", "done\n7\n"];
        yield return ["microtask-returns-pending", "queueMicrotask(()=>new Promise(()=>{}));console.log('done');", "done\n"];
        yield return ["timer-returns-pending", "setTimeout(()=>new Promise(()=>{}),1);console.log('done');", "done\n"];
        yield return ["timer-rejected-result", "process.on('unhandledRejection',(reason:any)=>console.log(reason));setTimeout(()=>Promise.reject('boom'),1);console.log('done');", "done\nboom\n"];
        yield return ["unref-timer-and-pending", "const timer:any=setTimeout(()=>console.log('unreachable'),10000);timer.unref();new Promise(()=>{}).then(()=>console.log('unreachable'));console.log('done');", "done\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PendingReactionsDoNotOwnProcessLifetime(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source, DecoratorMode.None));
    }
}
