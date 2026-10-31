using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalQueueMicrotaskValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-global-values", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const q=queueMicrotask;console.log(typeof q,typeof globalThis.queueMicrotask);",
        }, "function function\n"];
        yield return ["retained-global-value", new Dictionary<string,string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];const run=()=>{const q=queueMicrotask;q(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "helper\nsync,helper,global\n"];
        yield return ["fifo", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const q=queueMicrotask,events:string[]=[];q(()=>events.push(\"first\"));Promise.resolve().then(()=>events.push(\"promise\"));q(()=>events.push(\"last\"));events.push(\"sync\");setTimeout(()=>console.log(events.join(\",\")),0);",
        }, "sync,first,promise,last\n"];
        yield return ["return-and-metadata", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const q=queueMicrotask,g:any=globalThis;const d=Object.getOwnPropertyDescriptor(g,\"queueMicrotask\")!;console.log(q===g.queueMicrotask,q.name,q.length);console.log(d.value===q,d.writable,d.enumerable,d.configurable);console.log(q(()=>{})===undefined);",
        }, "true queueMicrotask 1\ntrue true true true\ntrue\n"];
        yield return ["call-apply-bind", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const q=queueMicrotask;q.call(null,()=>console.log(\"call\"));q.apply({},[()=>console.log(\"apply\")]);const bound=q.bind({});bound(()=>console.log(\"bound\"));console.log(\"sync\");",
        }, "sync\ncall\napply\nbound\n"];
        yield return ["async-value", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};async function run(){await 0;const q=queueMicrotask;q(()=>console.log(\"job\"));console.log(\"sync\");}run();",
        }, "sync\njob\n"];
        yield return ["generator-value", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};function* run(){yield 0;const q=queueMicrotask;q(()=>console.log(\"job\"));console.log(\"sync\");}const it=run();it.next();it.next();",
        }, "sync\njob\n"];
        yield return ["local-shadow", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};function queueMicrotask(callback:()=>void){console.log(\"local\");callback();}const q=queueMicrotask;console.log(q===globalThis.queueMicrotask);q(()=>console.log(\"callback\"));",
        }, "false\nlocal\ncallback\n"];
        yield return ["parameter-shadow", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const run=(queueMicrotask:(callback:()=>void)=>void)=>{const q=queueMicrotask;q(()=>console.log(\"callback\"));};run(callback=>{console.log(\"parameter\");callback();});",
        }, "parameter\ncallback\n"];
        yield return ["import-shadow", new Dictionary<string,string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void){console.log(\"import\");callback();}",
            ["main.ts"] = "import {queueMicrotask} from \"./lib\";const q=queueMicrotask;console.log(q===globalThis.queueMicrotask);q(()=>console.log(\"callback\"));",
        }, "false\nimport\ncallback\n"];
        yield return ["computed-global-only", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const g:any=globalThis;g[\"queueMicrotask\"](()=>console.log(\"job\"));console.log(\"sync\");",
        }, "sync\njob\n"];
        yield return ["replacement-read", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const saved=queueMicrotask,g:any=globalThis;g.queueMicrotask=(callback:()=>void)=>{console.log(\"replacement\");callback();};const q=queueMicrotask;console.log(q===g.queueMicrotask,q!==saved);q(()=>console.log(\"callback\"));",
        }, "true true\nreplacement\ncallback\n"];
        yield return ["deleted-binding", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const g:any=globalThis;delete g.queueMicrotask;console.log(typeof g.queueMicrotask);try{const q=queueMicrotask;console.log(q);}catch(error){console.log(error instanceof ReferenceError);}",
        }, "undefined\ntrue\n"];
        yield return ["abrupt-argument", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const q=queueMicrotask,marker={};function argument():()=>void{throw marker;}try{q(argument());}catch(error){console.log(error===marker);}",
        }, "true\n"];
        yield return ["callback-factory-alias", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const events:string[]=[];function callback(){events.push(\"arg\");return ()=>events.push(\"job\");}const q=queueMicrotask;q(callback());events.push(\"sync\");setTimeout(()=>console.log(events.join(\",\")),0);",
        }, "arg,sync,job\n"];
        yield return ["deleted-typeof", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};const g:any=globalThis;delete g.queueMicrotask;console.log(typeof queueMicrotask,typeof g.queueMicrotask);",
        }, "undefined undefined\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void GlobalFunctionValuesUseTheActualIntrinsic(string name, Dictionary<string,string> files, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }
}
