using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmErrorOriginTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["new-error", "try{vm.runInNewContext(\"throw new Error('boom')\",{},{filename:'unit-vm-origin.js'});}catch(error){console.log(String((error as any).stack).includes('unit-vm-origin.js'));}", "true\n"];
        yield return ["current-error", "try{vm.runInThisContext(\"throw new Error('boom')\",{filename:'unit-vm-origin.js'});}catch(error){console.log(String((error as any).stack).includes('unit-vm-origin.js'));}", "true\n"];
        yield return ["returned", "const error:any=vm.runInNewContext(\"new Error('kept')\",{},{filename:'returned-vm.js'});console.log(String(error.stack).includes('returned-vm.js'));", "true\n"];
        yield return ["caught-inside", "console.log(vm.runInNewContext(\"try{throw Error('x')}catch(e){String(e.stack).includes('caught-vm.js')}\",{},{filename:'caught-vm.js'}));", "true\n"];
        yield return ["script", "const error:any=new vm.Script(\"new TypeError('x')\",{filename:'script-vm.js'}).runInNewContext();console.log(String(error.stack).includes('script-vm.js'));", "true\n"];
        yield return ["function", "const error:any=vm.compileFunction(\"return new RangeError('x')\",[],{filename:'function-vm.js'})();console.log(String(error.stack).includes('function-vm.js'));", "true\n"];
        yield return ["external-error", "const original:any=new Error('outside');original.stack='outside-only';console.log(vm.runInNewContext(\"try{throw error}catch(e){e===error&&e.stack==='outside-only'}\",{error:original},{filename:'external-vm.js',displayErrors:false}));", "true\n"];
        yield return ["foreign-function", "const make=()=>new Error('outside');const error:any=vm.runInNewContext('make()',{make},{filename:'foreign-vm.js'});console.log(String(error.stack).split('\\n').slice(0,2).join('\\n').includes('foreign-vm.js'));", "false\n"];
        yield return ["nested", "const inner=()=>vm.runInNewContext(\"new Error('inner')\",{},{filename:'inner-vm.js'});const error:any=vm.runInNewContext('inner()',{inner},{filename:'outer-vm.js'});const first=String(error.stack).split('\\n').slice(0,2).join('\\n');console.log(first.includes('inner-vm.js'),first.includes('outer-vm.js'));", "true false\n"];
        yield return ["offset", "const error:any=vm.runInNewContext(\"new Error('x')\",{},{filename:'offset-vm.js',lineOffset:4,columnOffset:8});console.log(String(error.stack).includes('offset-vm.js:5:9'));", "true\n"];
        yield return ["stack-getter", "let reads=0;const arbitrary:any={};Object.defineProperty(arbitrary,'stack',{get(){reads++;throw 1;}});console.log(vm.runInNewContext('try{throw value}catch(e){e===value}',{value:arbitrary},{filename:'unread-vm.js',displayErrors:false}),reads);", "true 0\n"];
        yield return ["after-evaluate-async", "const context:any=vm.createContext({ok:false},{microtaskMode:'afterEvaluate'});vm.runInContext(\"async function make(){return new Error('async')}make().then(e=>{ok=String(e.stack).includes('async-vm.js')});0\",context,{filename:'async-vm.js'});console.log(context.ok);", "true\n"];
        yield return ["timeout", "try{vm.runInNewContext('while(true){}',{},{filename:'timeout-vm.js',timeout:20});}catch(error){console.log(String(error).includes('timed out'));}", "true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], "import * as vm from 'node:vm';" + row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RecordsTheActualErrorCreationOrigin(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
