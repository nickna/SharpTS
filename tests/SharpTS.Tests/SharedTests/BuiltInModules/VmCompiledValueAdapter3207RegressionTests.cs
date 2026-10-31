using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmCompiledValueAdapter3207RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-pending", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"new Promise(()=>{})\");console.log(typeof value,typeof value.then);", "object function\n"];
        yield return ["direct-undefined", "import * as vm from \"node:vm\";console.log(typeof vm.runInNewContext(\"undefined\"),vm.runInNewContext(\"null\")===null);", "undefined true\n"];
        yield return ["writeback-undefined", "import * as vm from 'vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var value=undefined;typeof value',ctx),Object.prototype.hasOwnProperty.call(ctx,'value'),typeof ctx.value);", "undefined true undefined\n"];
        yield return ["seed-undefined", "import * as vm from \"node:vm\";const ctx:any={value:undefined};console.log(vm.runInNewContext(\"value===undefined\",ctx));", "true\n"];
        yield return ["settled-number", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.resolve(7)\");value.then((result:any)=>console.log(result));", "7\n"];
        yield return ["settled-undefined", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.resolve(undefined)\");value.then((result:any)=>console.log(typeof result));", "undefined\n"];
        yield return ["rejected-undefined", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"Promise.reject(undefined)\");value.catch((reason:any)=>console.log(typeof reason));", "undefined\n"];
        yield return ["internal-throw-undefined", "import * as vm from \"node:vm\";try{vm.runInNewContext(\"throw undefined\");}catch(error){console.log(typeof error);}", "undefined\n"];
        yield return ["external-throw-identity", "import * as vm from \"node:vm\";const value:any={x:7};try{vm.runInNewContext(\"throw value\",{value});}catch(error){console.log(error===value);}", "true\n"];
        yield return ["repeated-promise", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const first:any=vm.runInContext(\"globalThis.p=new Promise(()=>{});p\",ctx);const second:any=vm.runInContext(\"p\",ctx);console.log(first===second,ctx.p===first);", "true true\n"];
        yield return ["reseed-actual-wrapper", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const first:any=vm.runInContext(\"globalThis.p=new Promise(()=>{});p\",ctx);ctx.alias=first;console.log(vm.runInContext(\"p===alias\",ctx),vm.runInContext(\"alias\",ctx)===first);", "true true\n"];
        yield return ["ordinary-promise-unchanged", "import * as vm from \"node:vm\";const first:any=Promise.resolve(7);console.log(vm.runInNewContext(\"value\",{value:first})===first);", "true\n"];
        yield return ["settled-reaction-order", "import * as vm from \"node:vm\";const inner:any=vm.runInNewContext(\"Promise.resolve(7)\");inner.then((value:any)=>console.log(\"inner\",value));Promise.resolve().then(()=>console.log(\"outer\"));console.log(\"sync\");", "sync\ninner 7\nouter\n"];
        yield return ["pending-reaction-order", "import * as vm from \"node:vm\";const inner:any=vm.runInNewContext(\"Promise.resolve().then(()=>7)\");inner.then((value:any)=>console.log(\"inner\",value));Promise.resolve().then(()=>console.log(\"outer\"));console.log(\"sync\");", "sync\nouter\ninner 7\n"];
        yield return ["foreign-ownership", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const p:any=vm.runInContext(\"Promise.resolve(7)\",ctx);const C:any=vm.runInContext(\"Promise\",ctx);console.log(p.constructor!==Promise,Object.getPrototypeOf(p)!==Promise.prototype,p.constructor===C);", "true true true\n"];
        yield return ["foreign-chain-ownership", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const p:any=vm.runInContext(\"Promise.resolve(7)\",ctx);const C:any=vm.runInContext(\"Promise\",ctx);const child:any=p.then((value:any)=>value+1);console.log(child.constructor===C,Object.getPrototypeOf(child)===Object.getPrototypeOf(p));child.then((value:any)=>console.log(value));", "true true\n8\n"];
        yield return ["foreign-species-chain", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const p:any=vm.runInContext(\"class P extends Promise{static get [Symbol.species](){return Promise;}};globalThis.P=P;P.resolve(7)\",ctx);const C:any=vm.runInContext(\"Promise\",ctx);const P:any=vm.runInContext(\"P\",ctx);const child:any=p.then((value:any)=>value);console.log(p.constructor===P,child.constructor===C,child.constructor!==P);", "true true true\n"];
        yield return ["returned-error-read", "import * as vm from \"node:vm\";const error:any=vm.runInNewContext(\"new Error(\\\"guest\\\")\",{},{filename:\"owned-error.js\"});console.log(error.message,typeof error.stack,error.stack.includes(\"owned-error.js\"));", "guest string true\n"];
        yield return ["returned-error-getter", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const error:any=vm.runInContext(\"globalThis.calls=0;const e=new Error();Object.defineProperty(e,\\\"stack\\\",{get(){calls++;return \\\"observed\\\";}});e\",ctx);console.log(error.stack,vm.runInContext(\"calls\",ctx));", "observed 1\n"];
        yield return ["pending-no-ref", "import * as vm from \"node:vm\";const value:any=vm.runInNewContext(\"new Promise(()=>{})\");value.then(()=>console.log(\"unreachable\"));console.log(\"done\");", "done\n"];
        yield return ["timer-promise-callback", "import * as vm from \"node:vm\";const result:any=vm.runInNewContext(\"new Promise(resolve=>setTimeout(()=>resolve(7),1))\",{setTimeout});result.then((value:any)=>console.log(value));", "7\n"];
        yield return ["timer-promise-rejected-undefined", "import * as vm from \"node:vm\";const result:any=vm.runInNewContext(\"new Promise((resolve,reject)=>setTimeout(()=>reject(undefined),1))\",{setTimeout});result.catch((value:any)=>console.log(typeof value));", "undefined\n"];
        yield return ["promise-method-identity", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const p:any=vm.runInContext(\"Promise.resolve(7)\",ctx);console.log(p.then===p.then,p.then===vm.runInContext(\"Promise.prototype.then\",ctx));", "true true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesActualVmValuesAndRealmOwnership(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Fact]
    public void RetainsForeignPromiseOwnerAfterCheckpointAndCollection()
    {
        const string source = "import * as vm from \"node:vm\";\n@DotNetType(\"System.GC\")\ndeclare class GarbageCollector {\n static Collect():void;\n static WaitForPendingFinalizers():void;\n}\nconst ctx:any=vm.createContext({});\nconst promise:any=vm.runInContext(\"Promise.resolve(7)\",ctx);\nsetTimeout(()=>{\n GarbageCollector.Collect();GarbageCollector.WaitForPendingFinalizers();GarbageCollector.Collect();\n const ctor:any=promise.constructor;\n console.log(ctor!==Promise,Object.getPrototypeOf(promise)!==Promise.prototype,ctor===vm.runInContext(\"Promise\",ctx));\n promise.then((value:any)=>console.log(value));\n},0);\r\n";
        Assert.Equal("true true true\n7\n", RunGcOwnershipSource(source));
    }

    private static string RunGcOwnershipSource(string source) => TestHarness.RunCompiledWithTimeout(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"vm_gc_{Guid.NewGuid():N}", "main.ts");
        var resolver = new SharpTS.Modules.ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path, SharpTS.Parsing.DecoratorMode.Legacy);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new SharpTS.TypeSystem.TypeChecker();
        checker.SetDecoratorMode(SharpTS.Parsing.DecoratorMode.Legacy);
        var types = checker.CheckModules(modules, resolver);
        var compiler = new SharpTS.Compilation.ILCompiler($"vm_gc_{Guid.NewGuid():N}");
        compiler.SetDecoratorMode(SharpTS.Parsing.DecoratorMode.Legacy);
        compiler.CompileModules(modules, resolver, types);
        return System.Reflection.Assembly.Load(compiler.SaveToBytes());
    }, TimeSpan.FromSeconds(30));
}