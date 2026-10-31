using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmLiveContextEnumerationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["seeded", "import * as vm from 'node:vm';const ctx:any={value:7};console.log(vm.runInNewContext('Object.keys(globalThis).includes(\"value\")',ctx),vm.runInNewContext('Object.values(globalThis).includes(7)',ctx),vm.runInNewContext('Object.entries(globalThis).some(([k,v])=>k===\"value\"&&v===7)',ctx));", "true true true\n"];
        yield return ["accessor-hidden", "import * as vm from 'node:vm';let calls=0;const ctx:any={};Object.defineProperty(ctx,'value',{get(){calls++;return 7},enumerable:true,configurable:true});Object.defineProperty(ctx,'hidden',{get(){calls++;return 9},enumerable:false});const g:any=vm.runInNewContext('globalThis',ctx);console.log(Object.keys(g).join(','),calls);console.log(Object.values(g).join(','),calls);console.log(JSON.stringify(Object.entries(g)),calls);", "value 0\n7 1\n[[\"value\",7]] 2\n"];
        yield return ["late-values", "import * as vm from 'node:vm';const ctx:any={};Object.defineProperty(ctx,'first',{get(){Object.defineProperty(ctx,'hidden',{enumerable:true});delete ctx.later;ctx.newer=4;return 1},enumerable:true,configurable:true});Object.defineProperty(ctx,'hidden',{value:2,enumerable:false,configurable:true});ctx.later=3;console.log(vm.runInNewContext('Object.values(globalThis).join(\",\")',ctx));", "1,2\n"];
        yield return ["late-entries", "import * as vm from 'node:vm';const ctx:any={};Object.defineProperty(ctx,'first',{get(){Object.defineProperty(ctx,'hidden',{enumerable:true});delete ctx.later;ctx.newer=4;return 1},enumerable:true,configurable:true});Object.defineProperty(ctx,'hidden',{value:2,enumerable:false,configurable:true});ctx.later=3;console.log(vm.runInNewContext('JSON.stringify(Object.entries(globalThis))',ctx));", "[[\"first\",1],[\"hidden\",2]]\n"];
        yield return ["seeded-numeric", "import * as vm from 'node:vm';const ctx:any={2:'two',0:'zero',1:'one',word:'w'};console.log(vm.runInNewContext('Object.keys(globalThis).join(\",\")',ctx));", "0,1,2,word\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EnumeratesActualContextKeysAndChecksLiveDescriptors(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }
}
