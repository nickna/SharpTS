using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

[Collection("ProcessLifecycleTests")]
public sealed class NativeReactionBindingTests : IDisposable
{
    public NativeReactionBindingTests() => ProcessBuiltIns.ResetProcessState();
    public void Dispose() => ProcessBuiltIns.ResetProcessState();
    public static IEnumerable<object[]> Cases()
    {
        yield return ["const-original", "const p=Promise.resolve(1).then(()=>{throw \"unused-reaction\"});console.log(\"after\");", "after\n", true, "unused-reaction"];
        yield return ["let-unused", "let p=Promise.resolve().then(()=>{throw \"let-bind\"});console.log(\"after\");", "after\n", true, "let-bind"];
        yield return ["var-unused", "var p=Promise.resolve().then(()=>{throw \"var-bind\"});console.log(\"after\");", "after\n", true, "var-bind"];
        yield return ["var-redeclaration", "var p:any;var p:any=Promise.resolve().then(()=>{throw \"redecl\"});console.log(\"after\");", "after\n", true, "redecl"];
        yield return ["async-local-binding", "async function run(){const p=Promise.resolve().then(()=>{throw \"async-bind\"});console.log(\"after\")}run();", "after\n", true, "async-bind"];
        yield return ["local-binding", "function run(){const p=Promise.resolve().then(()=>{throw \"local-bind\"});console.log(\"after\")}run();", "after\n", true, "local-bind"];
        yield return ["same-turn-catch", "const p=Promise.resolve().then(()=>{throw \"x\"});process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));p.catch(e=>console.log(\"caught\",e));", "caught x\n", false, ""];
        yield return ["explicit-await-consumer", "async function run(){const p=Promise.resolve().then(()=>{throw \"x\"});try{await p}catch(e){console.log(\"caught\",e)}}run();", "caught x\n", false, ""];
        yield return ["binding-and-discard-once", "const p=Promise.resolve().then(()=>{throw \"x\"});const q=p;process.on(\"unhandledRejection\",(e:any,r:any)=>console.log(e,r===p));p;q;", "x true\n", false, ""];
        yield return ["timer-backed-binding", "const p=new Promise(r=>setTimeout(()=>r(1),1)).then(()=>{throw \"timer-bind\"});console.log(\"after\");", "after\n", true, "timer-bind"];
        yield return ["never-settling-binding", "const p=new Promise(()=>{}).then(()=>{throw \"unexpected\"});console.log(\"done\");", "done\n", false, ""];
        yield return ["custom-capability-fulfillment", "function Custom(executor:any):any{return new Promise(executor)}(Custom as any).resolve=Promise.resolve;const p=(Promise.all as any).call(Custom,[1]);p.then((values:any)=>console.log(values[0]));", "1\n", false, ""];
        yield return ["named-assignment", "let p:any;p=Promise.resolve().then(()=>{throw \"assigned\"});console.log(\"after\");", "after\n", true, "assigned"];
        yield return ["hoisted-var-binding", "function run(){var p:any;{var p:any=Promise.resolve().then(()=>{throw \"hoisted\"})}console.log(\"after\")}run();", "after\n", true, "hoisted"];
    }

    [Theory, MemberData(nameof(Cases))]
    public async Task PublishedBindingReportsOnlyAnUnhandledNativeReaction(
        string name, string source, string expected, bool rejected, string diagnostic)
    {
        Assert.NotEmpty(name);
        var result = await Task.Factory.StartNew(() => DiscardedPromiseReactionTests.Capture(source),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .WaitAsync(TestHarness.DefaultTimeout);
        Assert.Equal(expected, result.Output);
        Assert.Equal(rejected, result.Rejected);
        if (rejected)
        {
            Assert.Contains(diagnostic, result.Error);
            Assert.Single(result.Error.Split('\n'), line => line.Contains(diagnostic, StringComparison.Ordinal));
        }
        else Assert.Empty(result.Error);
    }
}
