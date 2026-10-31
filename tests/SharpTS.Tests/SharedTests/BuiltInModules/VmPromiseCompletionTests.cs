using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmPromiseCompletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.resolve(7)\");console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["context-identity", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const value:any=vm.runInContext(\"var p=Promise.resolve(7);p\",ctx);console.log(typeof value,typeof value.then,value===ctx.p);", "object function true\n"];
        yield return ["script-new", "import * as vm from \"node:vm\";const value:any=new vm.Script(\"Promise.resolve(8)\").runInNewContext();console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["script-context", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const value:any=new vm.Script(\"var p=Promise.resolve(9);p\").runInContext(ctx);console.log(typeof value,typeof value.then,value===ctx.p);", "object function true\n"];
        yield return ["current-context", "import * as vm from \"node:vm\";const value:any=vm.runInThisContext(\"Promise.resolve(10)\");console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["script-current", "import * as vm from \"node:vm\";const value:any=new vm.Script(\"Promise.resolve(10)\").runInThisContext();console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["compiled-function", "import * as vm from \"node:vm\";const value:any=vm.compileFunction(\"return Promise.resolve(11)\")();console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["rejected", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.reject(7)\");value.catch(()=>{});console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["pending", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"new Promise(()=>{})\");console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["after-evaluate", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({flag:0},{microtaskMode:\"afterEvaluate\"});const value:any=vm.runInContext(\"Promise.resolve(7).then(value=>{flag=value;return value;})\",ctx);console.log(typeof value,ctx.flag);", "object 7\n"];
        yield return ["default-checkpoint", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({flag:0});vm.runInContext(\"Promise.resolve().then(()=>{flag=1;});0\",ctx);console.log(\"sync\",ctx.flag);Promise.resolve().then(()=>console.log(\"checkpoint\",ctx.flag));", "sync 0\ncheckpoint 1\n"];
        yield return ["ordinary-completion", "import * as vm from \"node:vm\";console.log(vm.runInNewContext(\"7;const x=1;\"),vm.runInNewContext(\"7;void 0;\"));", "7 undefined\n"];
        yield return ["fifo-chain","import * as vm from 'node:vm';async function main(){const log:string[]=[];vm.runInNewContext(\"Promise.resolve().then(()=>log.push('vm-a')).then(()=>log.push('vm-b'));0\",{log});Promise.resolve().then(()=>log.push('outer'));await new Promise<void>(resolve=>setTimeout(resolve,5));console.log(log.join(','));}main();","vm-a,outer,vm-b\n"];
        yield return ["thenable-chain","import * as vm from 'node:vm';async function main(){const log:string[]=[];vm.runInNewContext(\"Promise.resolve({then(resolve){log.push('thenable');resolve(7)}}).then(v=>log.push('value'+v));0\",{log});Promise.resolve().then(()=>log.push('outer'));await new Promise<void>(resolve=>setTimeout(resolve,5));console.log(log.join(','));}main();","thenable,outer,value7\n"];
        yield return ["timer","import * as vm from 'node:vm';async function main(){const log:string[]=[];const value:any=vm.runInNewContext(\"new Promise(resolve=>setTimeout(()=>{log.push('timer');resolve(7)},5))\",{setTimeout,log});console.log('sync',log.length);console.log(await value,log.join(','));}main();","sync 0\n7 timer\n"];
        yield return ["rejection-job","import * as vm from 'node:vm';async function main(){const log:string[]=[];vm.runInNewContext(\"Promise.reject(7).catch(value=>log.push('caught'+value));0\",{log});console.log('sync',log.length);await new Promise<void>(resolve=>setTimeout(resolve,5));console.log(log.join(','));}main();","sync 0\ncaught7\n"];
        yield return ["guest-abrupt-jobs","import * as vm from 'node:vm';async function main(){const log:string[]=[];try{vm.runInNewContext(\"Promise.resolve().then(()=>log.push('job'));throw 7\",{log});}catch(error){log.push('catch'+error);}console.log('sync',log.join(','));await new Promise<void>(resolve=>setTimeout(resolve,5));console.log('done',log.join(','));}main();","sync catch7\ndone catch7,job\n"];
        yield return ["sequential-contexts","import * as vm from 'node:vm';async function main(){const log:string[]=[];vm.runInNewContext(\"Promise.resolve().then(()=>log.push('one'));0\",{log});vm.runInNewContext(\"Promise.resolve().then(()=>log.push('two'));0\",{log});console.log('sync',log.length);await new Promise<void>(resolve=>setTimeout(resolve,5));console.log(log.join(','));}main();","sync 0\none,two\n"];
        yield return ["after-evaluate-timeout", "import * as vm from 'node:vm';const context:any=vm.createContext({},{microtaskMode:'afterEvaluate'});try{vm.runInContext('Promise.resolve().then(()=>{while(true){}});0',context,{timeout:20});console.log('returned');}catch(error){console.log(String(error).includes('timed out'));}", "true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesScriptCompletionAndMicrotaskPolicy(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }

    [Fact]
    public async Task ReplConventionRemainsSeparateFromScriptCompletion()
    {
        var statements = new Parser(new Lexer("Promise.resolve(7);").ScanTokens()).Parse();
        Assert.True(statements.IsSuccess);
        using var repl = new Interpreter(new StringWriter(), new StringWriter());
        new VariableResolver(repl).Resolve(statements.Statements);
        Assert.Equal(7d, Assert.IsType<double>(repl.InterpretRepl(statements.Statements)));
        using var script = new Interpreter(new StringWriter(), new StringWriter());
        var parsedScript = new Parser(new Lexer("Promise.resolve(7);").ScanTokens()).Parse();
        Assert.True(parsedScript.IsSuccess);
        new VariableResolver(script).Resolve(parsedScript.Statements);
        var promise = Assert.IsType<SharpTSPromise>(script.InterpretScriptCompletion(parsedScript.Statements));
        Assert.True(promise.Task.IsCompletedSuccessfully);
        Assert.Equal(7d, Assert.IsType<double>(await promise.Task));
    }
}
