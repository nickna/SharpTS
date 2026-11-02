using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> Issue3469BindingCases()
    {
        yield return ["const-original", "const p=Promise.resolve(1).then(()=>{throw \"unused-reaction\"});console.log(\"after\");", true, "after\n", "unused-reaction"];
        yield return ["let-unused", "let p=Promise.resolve().then(()=>{throw \"let-bind\"});console.log(\"after\");", true, "after\n", "let-bind"];
        yield return ["var-unused", "var p=Promise.resolve().then(()=>{throw \"var-bind\"});console.log(\"after\");", true, "after\n", "var-bind"];
        yield return ["var-redeclaration", "var p:any;var p:any=Promise.resolve().then(()=>{throw \"redecl\"});console.log(\"after\");", true, "after\n", "redecl"];
        yield return ["async-local-binding", "async function run(){const p=Promise.resolve().then(()=>{throw \"async-bind\"});console.log(\"after\")}run();", true, "after\n", "async-bind"];
        yield return ["local-binding", "function run(){const p=Promise.resolve().then(()=>{throw \"local-bind\"});console.log(\"after\")}run();", true, "after\n", "local-bind"];
        yield return ["same-turn-catch", "const p=Promise.resolve().then(()=>{throw \"x\"});process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));p.catch(e=>console.log(\"caught\",e));", false, "caught x\n", ""];
        yield return ["explicit-await-consumer", "async function run(){const p=Promise.resolve().then(()=>{throw \"x\"});try{await p}catch(e){console.log(\"caught\",e)}}run();", false, "caught x\n", ""];
        yield return ["binding-and-discard-once", "const p=Promise.resolve().then(()=>{throw \"x\"});const q=p;process.on(\"unhandledRejection\",(e:any,r:any)=>console.log(e,r===p));p;q;", false, "x true\n", ""];
        yield return ["timer-backed-binding", "const p=new Promise(r=>setTimeout(()=>r(1),1)).then(()=>{throw \"timer-bind\"});console.log(\"after\");", true, "after\n", "timer-bind"];
        yield return ["never-settling-binding", "const p=new Promise(()=>{}).then(()=>{throw \"unexpected\"});console.log(\"done\");", false, "done\n", ""];
        yield return ["custom-capability-fulfillment", "function Custom(executor:any):any{return new Promise(executor)}(Custom as any).resolve=Promise.resolve;const p=(Promise.all as any).call(Custom,[1]);p.then((values:any)=>console.log(values[0]));", false, "1\n", ""];
        yield return ["named-assignment", "let p:any;p=Promise.resolve().then(()=>{throw \"assigned\"});console.log(\"after\");", true, "after\n", "assigned"];
        yield return ["hoisted-var-binding", "function run(){var p:any;{var p:any=Promise.resolve().then(()=>{throw \"hoisted\"})}console.log(\"after\")}run();", true, "after\n", "hoisted"];
        yield return ["forward-no-handlers", "const p=Promise.reject(\"pass-through\");const q=p.then();process.on(\"unhandledRejection\",(e:any,r:any)=>console.log(e,r===q,r===p));q;", false, "pass-through true false\n", ""];
        yield return ["forward-fulfillment-only", "const p=Promise.reject(\"forward-fulfillment\");const q=p.then(v=>v);process.on(\"unhandledRejection\",(e:any,r:any)=>console.log(e,r===q,r===p));q;", false, "forward-fulfillment true false\n", ""];
        yield return ["forward-catch-omitted", "const p=Promise.reject(\"forward-catch\");const q=p.catch();process.on(\"unhandledRejection\",(e:any,r:any)=>console.log(e,r===q,r===p));q;", false, "forward-catch true false\n", ""];
        yield return ["forward-typed-rejection", "const p=Promise.reject<number>(\"typed\");const q=p.then(v=>v+1);process.on(\"unhandledRejection\",(e:any,r:any)=>console.log(e,r===q,r===p));q;", false, "typed true false\n", ""];
        yield return ["recursive-microtask-catch", "async function fail(){throw \"x\"}const p=fail();queueMicrotask(()=>queueMicrotask(()=>p.catch(e=>console.log(\"caught\",e))));console.log(\"after\");", false, "after\ncaught x\n", ""];
        yield return ["allsettled-consumes-input", "const p=Promise.reject(\"settled\");Promise.allSettled([p]).then(r=>console.log(r[0].status,(r[0] as any).reason));", false, "rejected settled\n", ""];
        yield return ["any-consumes-rejected-input", "const p=Promise.reject(\"skip\");Promise.any([p,Promise.resolve(7)]).then(v=>console.log(v));", false, "7\n", ""];
        yield return ["race-consumes-later-input", "const p=new Promise((r,j)=>setTimeout(()=>j(\"late\"),5));Promise.race([Promise.resolve(7),p]).then(v=>console.log(v));", false, "7\n", ""];
        yield return ["all-consumes-after-shortcircuit", "const first=Promise.reject(\"first\"),later=new Promise((r,j)=>setTimeout(()=>j(\"late\"),5));Promise.all([first,later]).catch(e=>console.log(e));", false, "first\n", ""];
        yield return ["vm-caught-completion", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.reject(7)\");value.catch(()=>{});console.log(typeof value,typeof value.then);", false, "object function\n", ""];
        yield return ["vm-late-handled-identity", "import * as vm from \"node:vm\";const p:any=vm.runInNewContext(`Promise.reject(\"vm-late\")`);process.on(\"unhandledRejection\",(e:any,q:any)=>{console.log(\"unhandled\",e,q===p);setTimeout(()=>p.catch(()=>console.log(\"caught\")),0)});process.on(\"rejectionHandled\",(q:any)=>console.log(\"handled\",q===p));", false, "unhandled vm-late true\ncaught\nhandled true\n", ""];
        yield return ["vm-catch-read-does-not-handle", "import * as vm from \"node:vm\";const p:any=vm.runInNewContext(`Promise.reject(\"vm-read\")`);const method=p.catch;process.on(\"unhandledRejection\",(e:any,q:any)=>console.log(e,q===p));", false, "vm-read true\n", ""];
    }

    [Theory, MemberData(nameof(Issue3469BindingCases))]
    public void Isolated_Issue3469PromiseBindingRejections(
        string name, string source, bool nonzero, string expectedStdout, string diagnostic)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath(name + ".dll");
        // node:vm executes through the existing interpreter-backed deployment.
        // Ordinary promise cases still prove standalone execution without Core.
        bool deployedVm = name.StartsWith("vm-", StringComparison.Ordinal);
        string deployment = deployedVm ? "" : "--standalone";
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --lib es2022,dom --compile \"{path}\" -o \"{output}\" {deployment} --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.Equal(deployedVm, File.Exists(directory.GetPath("SharpTS.dll")));
        var result = CaptureBoundedProcess(output, hidden: true);
        Assert.Equal(expectedStdout, result.Stdout);
        if (nonzero)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(diagnostic, result.Stderr);
        }
        else
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Stderr);
        }
    }
}
