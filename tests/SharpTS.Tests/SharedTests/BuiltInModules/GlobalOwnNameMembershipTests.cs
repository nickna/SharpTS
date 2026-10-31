using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class GlobalOwnNameMembershipTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-original","const target:any=globalThis;target.__p2_names__=7;console.log(Object.getOwnPropertyNames(target).includes('__p2_names__'));delete target.__p2_names__;","true\n"];
        yield return ["ordinary-reflect","const target:any=globalThis;target.__p2_keys__=7;console.log(Reflect.ownKeys(target).includes('__p2_keys__'));delete target.__p2_keys__;","true\n"];
        yield return ["context-names","import * as vm from 'node:vm';const ctx:any={value:7};console.log(vm.runInNewContext(\"Object.getOwnPropertyNames(globalThis).includes(\\\"value\\\")\",ctx));","true\n"];
        yield return ["context-own-keys","import * as vm from 'node:vm';const ctx:any={value:7};console.log(vm.runInNewContext(\"Reflect.ownKeys(globalThis).includes(\\\"value\\\")\",ctx));","true\n"];
        yield return ["private-order","import * as vm from 'node:vm';const ctx:any={1:\"one\",word:\"w\"};console.log(vm.runInNewContext(\"var local;globalThis[2]=\\\"two\\\";globalThis[0]=\\\"zero\\\";Object.getOwnPropertyNames(globalThis).filter(key=>[\\\"0\\\",\\\"1\\\",\\\"2\\\",\\\"local\\\",\\\"word\\\"].includes(key)).join(\\\",\\\")\",ctx));","0,2,1,local,word\n"];
        yield return ["reflect-order","import * as vm from 'node:vm';const ctx:any={1:\"one\",word:\"w\"};console.log(vm.runInNewContext(\"var local;globalThis[2]=\\\"two\\\";globalThis[0]=\\\"zero\\\";Reflect.ownKeys(globalThis).filter(key=>[\\\"0\\\",\\\"1\\\",\\\"2\\\",\\\"local\\\",\\\"word\\\"].includes(key)).join(\\\",\\\")\",ctx));","0,2,1,local,word\n"];
        yield return ["no-getter","import * as vm from 'node:vm';const ctx:any={value:7};console.log(vm.runInNewContext(\"let calls=0;Object.defineProperty(globalThis,\\\"accessor\\\",{get(){calls++;throw 7;},enumerable:false,configurable:true});const names=Object.getOwnPropertyNames(globalThis);const keys=Reflect.ownKeys(globalThis);names.includes(\\\"accessor\\\")+\\\":\\\"+keys.includes(\\\"accessor\\\")+\\\":\\\"+calls\",ctx));","true:true:0\n"];
        yield return ["delete-live","import * as vm from 'node:vm';const ctx:any={value:7};console.log(vm.runInNewContext(\"globalThis.added=1;const before=Object.getOwnPropertyNames(globalThis).includes(\\\"added\\\");delete globalThis.added;before+\\\":\\\"+Object.getOwnPropertyNames(globalThis).includes(\\\"added\\\")+\\\":\\\"+Reflect.ownKeys(globalThis).includes(\\\"added\\\")\",ctx));","true:false:false\n"];
        yield return ["readonly-hidden","import * as vm from 'node:vm';const ctx:any={value:7};console.log(vm.runInNewContext(\"Object.defineProperty(globalThis,\\\"hidden\\\",{value:1,writable:false,enumerable:false,configurable:false});Object.getOwnPropertyNames(globalThis).includes(\\\"hidden\\\")+\\\":\\\"+Reflect.ownKeys(globalThis).includes(\\\"hidden\\\")+\\\":\\\"+Object.keys(globalThis).includes(\\\"hidden\\\")\",ctx));","true:true:false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void QueriesActualGlobalStringKeysWithoutReadingValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
