using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmContextStorage3251Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["readonly", "import * as vm from 'node:vm';const ctx:any={};Object.defineProperty(ctx,'foo',{value:7,writable:false,enumerable:true,configurable:true});console.log(vm.runInNewContext('foo=9;String(foo)',ctx),ctx.foo);", "7 7\n"];
        yield return ["hidden", "import * as vm from 'node:vm';const ctx:any={};Object.defineProperty(ctx,'foo',{value:7,writable:true,enumerable:false,configurable:true});console.log(vm.runInNewContext('String(Object.getOwnPropertyDescriptor(globalThis,\"foo\").enumerable)',ctx));", "false\n"];
        yield return ["nonextensible", "import * as vm from 'node:vm';const ctx:any=Object.preventExtensions({});console.log(vm.runInNewContext('globalThis.foo=1;String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "1 false\n"];
        yield return ["new-vm-accessor", "import * as vm from 'node:vm';const ctx:any={};vm.runInNewContext('Object.defineProperty(globalThis,\"x\",{get(){return 7;},enumerable:false,configurable:true})',ctx);const d=Object.getOwnPropertyDescriptor(ctx,'x')!;console.log(ctx.x,d.enumerable,d.configurable,typeof d.get,d.get===Object.getOwnPropertyDescriptor(ctx,'x')!.get);", "7 false true function true\n"];
        yield return ["setter-original-receiver-once", "import * as vm from 'node:vm';const ctx:any={};let calls=0;Object.defineProperty(ctx,'x',{set(value){calls++;console.log(this===ctx,value);},configurable:true});vm.runInNewContext('x=7',ctx);console.log(calls);", "true 7\n1\n"];
        yield return ["vm-created-getter-abrupt-identity", "import * as vm from 'node:vm';const marker={};let calls=0;const ctx:any={marker,notify:()=>{calls++;}};vm.runInNewContext('Object.defineProperty(globalThis,\"x\",{get(){notify();throw marker;},configurable:true})',ctx);try{ctx.x;}catch(error){console.log(error===marker,calls);}", "true 1\n"];
        yield return ["partial-descriptor-fields", "import * as vm from 'node:vm';const ctx:any={x:7};vm.runInNewContext('Object.defineProperty(globalThis,\"x\",{enumerable:false})',ctx);const d=Object.getOwnPropertyDescriptor(ctx,'x')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "7 true false true\n"];
        yield return ["inferred-record", "import * as vm from \"node:vm\";const ctx={foo:7};vm.runInNewContext(\"foo=9\",ctx);console.log(ctx.foo);", "9\n"];
        yield return ["json-typed-record", "import * as vm from \"node:vm\";const ctx={foo:7,word:\"x\"};console.log(JSON.stringify(ctx));vm.runInNewContext('Object.defineProperty(globalThis,\"foo\",{value:9})',ctx);console.log(ctx.foo,JSON.stringify(ctx));", "{\"foo\":7,\"word\":\"x\"}\n9 {\"foo\":9,\"word\":\"x\"}\n"];
        yield return ["async-literal", "import * as vm from \"node:vm\";async function make(){return {foo:await Promise.resolve(7)};}make().then(ctx=>{vm.runInNewContext(\"foo=9\",ctx);console.log(ctx.foo);});", "9\n"];
        yield return ["generator-literal", "import * as vm from \"node:vm\";function* make():any{return {foo:yield 7};}const iterator=make();console.log(iterator.next().value);const ctx:any=iterator.next(9).value;vm.runInNewContext(\"foo=10\",ctx);console.log(ctx.foo);", "7\n10\n"];
        yield return ["spread-computed-literal", "import * as vm from \"node:vm\";const key=\"foo\";const ctx:any={...{foo:7},[key+\"2\"]:8};vm.runInNewContext(\"foo=9;foo2=10\",ctx);console.log(ctx.foo,ctx.foo2);", "9 10\n"];
    }
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));
    [Theory, MemberData(nameof(Modes))]
    public void UsesActualContextStorageWithoutLosingDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
