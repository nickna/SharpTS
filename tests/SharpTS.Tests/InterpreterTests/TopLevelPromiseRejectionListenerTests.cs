using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

[Collection("ProcessLifecycleTests")]
public sealed class TopLevelPromiseRejectionListenerTests : IDisposable
{
    public TopLevelPromiseRejectionListenerTests() => ProcessBuiltIns.ResetProcessState();
    public void Dispose() => ProcessBuiltIns.ResetProcessState();

    public static IEnumerable<object[]> Cases()
    {
        yield return ["listener-receives-string-original", "process.on(\"unhandledRejection\",(reason:any)=>console.log(typeof reason,reason));Promise.reject(\"DataCloneError: guest\");", "string DataCloneError: guest\n"];
        yield return ["async-clone-listener-original", "process.on(\"unhandledRejection\",()=>console.log(\"handled\"));async function fail(){structuredClone(()=>{})}fail();", "handled\n"];
        yield return ["object-reason-identity", "const reason={token:1};process.on(\"unhandledRejection\",(e:any)=>console.log(e===reason,e.token));Promise.reject(reason);", "true 1\n"];
        yield return ["error-reason-identity", "const reason=new Error(\"x\");process.on(\"unhandledRejection\",(e:any)=>console.log(e===reason,e.name,e.message));Promise.reject(reason);", "true Error x\n"];
        yield return ["null-reason", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e));Promise.reject(null);", "object null\n"];
        yield return ["undefined-reason", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e));Promise.reject(undefined);", "undefined undefined\n"];
        yield return ["number-reason", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e));Promise.reject(7);", "number 7\n"];
        yield return ["boolean-reason", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e));Promise.reject(false);", "boolean false\n"];
        yield return ["bigint-reason-identity", "const reason=3n;process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e===reason));Promise.reject(reason);", "bigint true\n"];
        yield return ["symbol-reason-identity", "const reason=Symbol(\"s\");process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e===reason));Promise.reject(reason);", "symbol true\n"];
        yield return ["promise-identity", "const reason={};let p:any;process.on(\"unhandledRejection\",(e:any,q:any)=>console.log(e===reason,q===p));p=Promise.reject(reason);", "true true\n"];
        yield return ["statement-before-listener-delivery", "process.on(\"unhandledRejection\",(e:any)=>console.log(\"handled\",e));Promise.reject(\"x\");console.log(\"after\");", "after\nhandled x\n"];
        yield return ["pending-timer-rejection", "const reason={};process.on(\"unhandledRejection\",(e:any)=>console.log(e===reason));new Promise((resolve,reject)=>{setTimeout(()=>reject(reason),1)});console.log(\"after\");", "after\ntrue\n"];
        yield return ["async-await-guest-rejection", "const reason={};process.on(\"unhandledRejection\",(e:any)=>console.log(e===reason));async function fail(){await Promise.resolve();throw reason}fail();", "true\n"];
        yield return ["distinct-rejections", "process.on(\"unhandledRejection\",(e:any)=>console.log(e));Promise.reject(\"a\");Promise.reject(\"b\");", "a\nb\n"];
        yield return ["explicit-await-catch", "const reason={};process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));async function run(){try{await Promise.reject(reason)}catch(e){console.log(e===reason)}}run();", "true\n"];
        yield return ["explicit-catch-control", "const reason={};process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));Promise.reject(reason).catch(e=>console.log(e===reason));", "true\n"];
        yield return ["fulfilled-control", "process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));Promise.resolve(7);console.log(\"done\");", "done\n"];
        yield return ["never-settling-idle", "process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));new Promise(()=>{});console.log(\"done\");", "done\n"];
        yield return ["once-listener", "process.once(\"unhandledRejection\",(e:any)=>console.log(e));Promise.reject(\"x\");", "x\n"];
        yield return ["listener-this-receiver", "process.on(\"unhandledRejection\",function(this:any,e:any){console.log(this===process,e)});Promise.reject(7);", "true 7\n"];
        yield return ["custom-capability-fulfillment", "process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));function Custom(executor:any):any{return new Promise(executor)}(Custom as any).resolve=Promise.resolve;(Promise.all as any).call(Custom,[1]).then((values:any)=>console.log(values[0]));", "1\n"];
        yield return ["same-promise-discarded-twice", "const p=Promise.reject(\"x\");process.on(\"unhandledRejection\",(e:any,q:any)=>console.log(e,q===p));p;p;", "x true\n"];
        yield return ["same-turn-make-catch", "let p:any;process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));process.on(\"rejectionHandled\",()=>console.log(\"unexpected handled\"));function make(){p=Promise.reject(\"x\");return p}make();p.catch(()=>console.log(\"caught\"));", "caught\n"];
        yield return ["same-turn-alias-catch", "const p=Promise.reject(\"x\");process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));process.on(\"rejectionHandled\",()=>console.log(\"unexpected handled\"));p;p.catch(()=>console.log(\"caught\"));", "caught\n"];
        yield return ["nominal-clone-listener-value", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e.name,e instanceof Error));async function fail(){structuredClone(()=>{})}fail();", "object DataCloneError true\n"];
        yield return ["late-catch-after-report", "const p=Promise.reject(\"x\");process.on(\"unhandledRejection\",(e:any,q:any)=>{console.log(\"unhandled\",e,q===p);setTimeout(()=>p.catch(()=>console.log(\"caught\")),0)});process.on(\"rejectionHandled\",(q:any)=>console.log(\"handled\",q===p));p;", "unhandled x true\ncaught\nhandled true\n"];
        yield return ["same-turn-explicit-await", "let p:any;process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));process.on(\"rejectionHandled\",()=>console.log(\"unexpected handled\"));function make(){p=Promise.reject(\"x\");return p}make();async function consume(){try{await p}catch(e){console.log(\"caught\",e)}}consume();", "caught x\n"];
        yield return ["esm-module-listener", "process.on(\"unhandledRejection\",(e:any)=>console.log(\"handled\",e));Promise.reject(\"x\");console.log(\"after\");export {};", "after\nhandled x\n"];
    }

    public static IEnumerable<object[]> ModuleCases()
    {
        yield return ["commonjs-entry-listener", "require(\"./dep\");process.on(\"unhandledRejection\",(e:any)=>console.log(\"handled\",e));Promise.reject(\"x\");console.log(\"after\");", "module.exports=7;", "after\nhandled x\n"];
        yield return ["commonjs-required-listener", "process.on(\"unhandledRejection\",(e:any)=>console.log(\"handled\",e));require(\"./dep\");console.log(\"after\");", "Promise.reject(\"x\");module.exports=7;", "after\nhandled x\n"];
    }

    [Theory, MemberData(nameof(ModuleCases))]
    public void CommonJsEntryAndRequiredModuleKeepTheListener(string name, string main, string dependency, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string>
            { ["main.ts"] = main, ["dep.js"] = dependency }, "main.ts", ExecutionMode.Interpreted));
    }

    [Theory, MemberData(nameof(Cases))]
    public void ExistingListenerReceivesDiscardedRejection(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        // The ESM row must exercise ExecuteModule, rather than the single-script helper.
        var output = name == "esm-module-listener"
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source },
                "main.ts", ExecutionMode.Interpreted)
            : TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal(expected, output);
    }
}
