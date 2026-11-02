using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

[Collection("ProcessLifecycleTests")]
public sealed class DiscardedPromiseReactionTests : IDisposable
{
    public DiscardedPromiseReactionTests() => ProcessBuiltIns.ResetProcessState();
    public void Dispose() => ProcessBuiltIns.ResetProcessState();

    public static IEnumerable<object[]> Cases()
    {
        yield return ["unhandled-reaction-original", "Promise.resolve(1).then(()=>structuredClone(()=>{}));", "", true, "DataCloneError"];
        yield return ["throw-string", "Promise.resolve().then(()=>{throw \"guest-marker\"});", "", true, "guest-marker"];
        yield return ["return-rejected-promise", "Promise.resolve().then(()=>Promise.reject(\"guest-marker\"));", "", true, "guest-marker"];
        yield return ["catch-throws", "Promise.reject(\"before\").catch(()=>{throw \"guest-marker\"});", "", true, "guest-marker"];
        yield return ["finally-throws", "Promise.resolve().finally(()=>{throw \"guest-marker\"});", "", true, "guest-marker"];
        yield return ["finally-preserves-rejection", "Promise.reject(\"guest-marker\").finally(()=>{});", "", true, "guest-marker"];
        yield return ["statement-before-fatal", "Promise.resolve().then(()=>{throw \"guest-marker\"});console.log(\"after\");", "after\n", true, "guest-marker"];
        yield return ["listener-retains-guest-string", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e));Promise.resolve().then(()=>{throw \"DataCloneError: guest\"});", "string DataCloneError: guest\n", false, ""];
        yield return ["listener-nominal-host-error", "process.on(\"unhandledRejection\",(e:any)=>console.log(typeof e,e.name,e instanceof Error));Promise.resolve().then(()=>structuredClone(()=>{}));", "object DataCloneError true\n", false, ""];
        yield return ["listener-reason-and-promise-identity", "const reason={token:7};let p:any;process.on(\"unhandledRejection\",(e:any,q:any)=>console.log(e===reason,q===p));p=Promise.resolve().then(()=>{throw reason});p;", "true true\n", false, ""];
        yield return ["same-promise-discarded-twice", "let p:any;process.on(\"unhandledRejection\",(e:any,q:any)=>console.log(e,q===p));p=Promise.resolve().then(()=>{throw \"x\"});p;p;", "x true\n", false, ""];
        yield return ["same-turn-catch", "const p=Promise.resolve().then(()=>{throw \"x\"});process.on(\"unhandledRejection\",()=>console.log(\"unexpected\"));process.on(\"rejectionHandled\",()=>console.log(\"unexpected handled\"));p;p.catch(e=>console.log(\"caught\",e));", "caught x\n", false, ""];
        yield return ["handled-chain", "Promise.resolve().then(()=>{throw \"x\"}).catch(e=>console.log(\"caught\",e));", "caught x\n", false, ""];
        yield return ["explicit-await-catch", "async function run(){try{await Promise.resolve().then(()=>{throw \"x\"})}catch(e){console.log(\"caught\",e)}}run();", "caught x\n", false, ""];
        yield return ["fulfilled-fifo", "Promise.resolve(1).then(v=>console.log(\"reaction\",v));console.log(\"after\");", "after\nreaction 1\n", false, ""];
        yield return ["never-settling-idle", "new Promise(()=>{}).then(()=>{throw \"unexpected\"});console.log(\"done\");", "done\n", false, ""];
        yield return ["timer-backed-reaction", "process.on(\"unhandledRejection\",(e:any)=>console.log(e));new Promise(r=>setTimeout(()=>r(1),1)).then(()=>{throw \"timer\"});console.log(\"after\");", "after\ntimer\n", false, ""];
        yield return ["late-catch-after-report", "let p:any;process.on(\"unhandledRejection\",(e:any,q:any)=>{console.log(\"unhandled\",e,q===p);setTimeout(()=>p.catch(()=>console.log(\"caught\")),0)});process.on(\"rejectionHandled\",(q:any)=>console.log(\"handled\",q===p));p=Promise.resolve().then(()=>{throw \"x\"});p;", "unhandled x true\ncaught\nhandled true\n", false, ""];
        yield return ["esm-reaction-default", "Promise.resolve(1).then(()=>structuredClone(()=>{}));export {};", "", true, "DataCloneError"];
        yield return ["custom-capability-fulfillment", "function Custom(executor:any):any{return new Promise(executor)}(Custom as any).resolve=Promise.resolve;(Promise.all as any).call(Custom,[1]).then((values:any)=>console.log(values[0]));", "1\n", false, ""];
        yield return ["same-promise-default-report-once", "let p=Promise.resolve().then(()=>{throw \"duplicate-marker\"});p;p;", "", true, "duplicate-marker"];
    }

    [Theory, MemberData(nameof(Cases))]
    public async Task DiscardedNativeReactionReportsOnlyAnUnhandledRejection(
        string name, string source, string expected, bool rejected, string diagnostic)
    {
        Assert.NotEmpty(name);
        var result = await Task.Factory.StartNew(() => Capture(source),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .WaitAsync(TestHarness.DefaultTimeout);
        Assert.Equal(expected, result.Output);
        Assert.Equal(rejected, result.Rejected);
        if (rejected)
        {
            Assert.Contains(diagnostic, result.Error);
            Assert.Single(result.Error.Split('\n'), line => line.StartsWith("Unhandled promise rejection:", StringComparison.Ordinal));
        }
        else Assert.Empty(result.Error);
    }

    internal static (string Output, string Error, bool Rejected) Capture(string source)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var interpreter = new Interpreter(stdout: output, stderr: error);
        interpreter.EmitProcessLifecycleEvents = true;
        var checker = new TypeChecker();
        if (source.EndsWith("export {};", StringComparison.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "sharpts_reaction_" + Guid.NewGuid().ToString("N"), "main.ts"));
            var resolver = new ModuleResolver(path,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [path] = source });
            var modules = resolver.GetModulesInOrder(resolver.LoadModule(path));
            var map = TestHarness.CheckModulesOrThrow(checker, modules, resolver);
            interpreter.InterpretModules(modules, resolver, map);
        }
        else
        {
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            interpreter.Interpret(statements, checker.Check(statements));
        }
        return (output.ToString().Replace("\r\n", "\n"),
            error.ToString().Replace("\r\n", "\n"), interpreter.HadUnhandledRejection);
    }
}
