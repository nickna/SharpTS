using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmPromiseCreationRealmTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["async-arrow", "import * as vm from 'node:vm';async function main(){const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('async()=>{await 0;return Promise}',ctx);const outer:any=Promise;console.log(await f()===C,Promise===outer);}main();", "true true\n"];
        yield return ["parenthesized-async-arrow", "import * as vm from 'node:vm';async function main(){const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('(async()=>{await 0;return Promise})',ctx);const outer:any=Promise;console.log(await f()===C,Promise===outer);}main();", "true true\n"];
        yield return ["original", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});const p:any=vm.runInContext(\"class P extends Promise{static get [Symbol.species](){return Promise;}};globalThis.P=P;new P(()=>{})\",ctx);const C:any=vm.runInContext(\"Promise\",ctx);const P:any=vm.runInContext(\"P\",ctx);const child:any=p.then((value:any)=>value);console.log(p.constructor===P,child.constructor===C,child.constructor!==P);", "true true true\n"];
        yield return ["inherited-species", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const p:any=vm.runInContext('class P extends Promise{};globalThis.P=P;new P(()=>{})',ctx);const P:any=vm.runInContext('P',ctx);console.log(p.then(()=>1).constructor===P);", "true\n"];
        yield return ["function-and-bound", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('(function(){return Promise})',ctx);const outer:any=Promise;console.log(f()===C,f.bind(null)()===C,Promise===outer);", "true true true\n"];
        yield return ["arrow-and-default", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const arrow:any=vm.runInContext('()=>Promise',ctx);const defaults:any=vm.runInContext('(value=Promise)=>value',ctx);console.log(arrow()===C,defaults()===C);", "true true\n"];
        yield return ["declaration-default", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('function make(value=Promise){return value};make',ctx);console.log(f()===C,f.call(null)===C);", "true true\n"];
        yield return ["borrowed-getter", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const getter:any=vm.runInContext('()=>Promise',ctx);class Local extends Promise<any>{}Object.defineProperty(Local,Symbol.species,{get:getter});const p:any=new Local(()=>{});console.log(p.then(()=>1).constructor===C,Promise!==C);", "true true\n"];
        yield return ["reentrant-caller", "import * as vm from 'node:vm';const outer:any=Promise;const readOuter=()=>Promise;const ctx:any=vm.createContext({readOuter});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('(function(){return [Promise,readOuter()]})',ctx);const values:any=f();console.log(values[0]===C,values[1]===outer,Promise===outer);", "true true true\n"];
        yield return ["abrupt-getter", "import * as vm from 'node:vm';const marker:any={x:1};const outer:any=Promise;const ctx:any=vm.createContext({marker});const p:any=vm.runInContext('class P extends Promise{static get [Symbol.species](){throw marker}};new P(()=>{})',ctx);try{p.then(()=>1)}catch(e){console.log(e===marker,Promise===outer);}", "true true\n"];
        yield return ["async-default", "import * as vm from 'node:vm';async function main(){const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('async function make(value=Promise){await 0;return value};make',ctx);const outer:any=Promise;console.log(await f()===C,Promise===outer);}main();", "true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PromiseLookupUsesTheActualCapturedEnvironment(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }
}
