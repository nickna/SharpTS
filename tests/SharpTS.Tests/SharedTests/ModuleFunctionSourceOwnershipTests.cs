using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ModuleFunctionSourceOwnershipTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-arrow", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];const run=()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "helper\nsync,helper,global\n"];
        yield return ["top-level-control", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);",
        }, "helper\nsync,helper,global\n"];
        yield return ["named-function-control", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];function run(){queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "helper\nsync,helper,global\n"];
        yield return ["nested-arrow", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];const outer=()=>()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};outer()();",
        }, "helper\nsync,helper,global\n"];
        yield return ["async-arrow", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];const run=async()=>{await 0;queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "helper\nsync,helper,global\n"];
        yield return ["generator-body", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];function* work(){yield 0;queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);}const it=work();it.next();it.next();",
        }, "helper\nsync,helper,global\n"];
        yield return ["local-function-shadow", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];function queueMicrotask(cb:()=>void){console.log(\"local\");cb();}const run=()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "local\nhelper\nglobal,sync,helper\n"];
        yield return ["parameter-shadow", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];const run=(queueMicrotask:(cb:()=>void)=>void)=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run(cb=>{console.log(\"parameter\");cb();});",
        }, "parameter\nhelper\nglobal,sync,helper\n"];
        yield return ["private-foreign-function", new Dictionary<string, string>
        {
            ["lib.ts"] = "function queueMicrotask(cb:()=>void){console.log(\"private\");cb();}export function enqueue(cb:()=>void){queueMicrotask(cb);}",
            ["main.ts"] = "import {enqueue} from \"./lib\";const events:string[]=[];const run=()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "private\nsync,helper,global\n"];
        yield return ["multiple-import-owners", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(cb:()=>void){console.log(\"a\");cb();}",
            ["other.ts"] = "export function queueMicrotask(cb:()=>void){console.log(\"b\");cb();}",
            ["main.ts"] = "import {queueMicrotask as a} from \"./lib\";import {queueMicrotask as b} from \"./other\";const events:string[]=[];const run=()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");a(()=>events.push(\"a\"));b(()=>events.push(\"b\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "a\nb\nsync,a,b,global\n"];
        yield return ["foreign-sibling-call", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function queueMicrotask(cb:()=>void){console.log(\"sibling\");cb();}export function enqueue(cb:()=>void){queueMicrotask(cb);}",
            ["main.ts"] = "import {enqueue} from \"./lib\";const events:string[]=[];const run=()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};run();",
        }, "sibling\nsync,helper,global\n"];
        yield return ["same-name-source-functions", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function same(){return 2;}",
            ["other.ts"] = "export function same(){return 3;}",
            ["main.ts"] = "import {same as first} from \"./lib\";import {same as second} from \"./other\";function same(){return 1;}const run=()=>console.log(same(),first(),second());run();",
        }, "1 2 3\n"];
        yield return ["other-global-function-value", new Dictionary<string, string>
        {
            ["lib.ts"] = "export function parseInt(text:string){return 99;}",
            ["main.ts"] = "import {parseInt as helper} from \"./lib\";const run=()=>{const f=parseInt;console.log(f(\"7\"),helper(\"7\"));};run();",
        }, "7 99\n"];
        yield return ["same-name-module-loop-captures", new Dictionary<string,string>
        {
            ["lib.ts"] = "export const values:(()=>number)[]=[];for(let i=0;i<3;i++)values.push(()=>i);",
            ["other.ts"] = "export const values:(()=>number)[]=[];for(let i=10;i<13;i++)values.push(()=>i);",
            ["main.ts"] = "import {values} from \"./lib\";import {values as other} from \"./other\";console.log(values.map(f=>f()).join(\",\"),other.map(f=>f()).join(\",\"));",
        }, "0,1,2 10,11,12\n"];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptProgramKeepsCrossFileFunctionsVisible(bool native)
    {
        string root = Path.Combine(Path.GetTempPath(), "script_owners_" + Guid.NewGuid().ToString("N"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(root, "shared.ts")] = "function helper(){return 7;}function second(){return 8;}",
            [Path.Combine(root, "main.ts")] = "/// <reference path=\"./shared.ts\" />\nconst run=()=>console.log(helper(),named());function named(){return second();}run();"
        };
        var resolver = new ModuleResolver(files.Keys.Last(), files);
        var modules = files.Keys.Select(path => resolver.LoadModule(path)).ToList();
        var types = TestHarness.CheckModulesOrThrow(new TypeChecker(), modules, resolver);
        var compiler = new ILCompiler("script_owners_" + Guid.NewGuid().ToString("N"));
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, types);
        var image = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(image)));
        Assert.Equal("7 8\n", TestHarness.RunCompiledWithTimeout(() => Assembly.Load(image), TimeSpan.FromSeconds(20)));
    }
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void FunctionLookupsUseOnlyTheCurrentSourceModule(string name, Dictionary<string, string> files, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }
}
