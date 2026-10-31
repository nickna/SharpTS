using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmGlobalKeyOrderingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original","import * as vm from 'node:vm';const ctx:any={1:'one',word:'w'};console.log(vm.runInNewContext('var local;globalThis[2]=\"two\";globalThis[0]=\"zero\";Object.keys(globalThis).filter(key=>[\"0\",\"1\",\"2\",\"local\",\"word\"].includes(key)).join(\",\")',ctx));","0,2,1,local,word\n"];
        yield return ["two-index-stores","import * as vm from 'node:vm';const ctx:any={1:\"one\",5:\"five\",word:\"w\"};console.log(vm.runInNewContext(\"globalThis[9]=\\\"nine\\\";globalThis[2]=\\\"two\\\";Object.keys(globalThis).filter(key=>[\\\"1\\\", \\\"2\\\", \\\"5\\\", \\\"9\\\", \\\"word\\\"].includes(key)).join(\\\",\\\")\",ctx));","2,9,1,5,word\n"];
        yield return ["string-stores","import * as vm from 'node:vm';const ctx:any={first:1,last:2};console.log(vm.runInNewContext(\"var local;globalThis.added=3;Object.keys(globalThis).filter(key=>[\\\"first\\\", \\\"last\\\", \\\"local\\\", \\\"added\\\"].includes(key)).join(\\\",\\\")\",ctx));","local,added,first,last\n"];
        yield return ["written-seeded-name","import * as vm from 'node:vm';const ctx:any={first:1,last:2};console.log(vm.runInNewContext(\"globalThis.last=3;globalThis.added=4;Object.keys(globalThis).filter(key=>[\\\"first\\\", \\\"last\\\", \\\"added\\\"].includes(key)).join(\\\",\\\")\",ctx));","last,added,first\n"];
        yield return ["delete-recreate","import * as vm from 'node:vm';const ctx:any={seed:1};console.log(vm.runInNewContext(\"globalThis.a=1;globalThis.b=2;delete globalThis.a;globalThis.a=3;Object.keys(globalThis).filter(key=>[\\\"a\\\", \\\"b\\\", \\\"seed\\\"].includes(key)).join(\\\",\\\")\",ctx));","b,a,seed\n"];
        yield return ["index-boundaries","import * as vm from 'node:vm';const ctx:any={0:\"zero\",word:1};console.log(vm.runInNewContext(\"globalThis[2]=\\\"two\\\";globalThis[\\\"4294967294\\\"]=\\\"max\\\";globalThis[\\\"4294967295\\\"]=\\\"ordinary\\\";globalThis[\\\"01\\\"]=\\\"ordinary\\\";Object.keys(globalThis).filter(key=>[\\\"0\\\", \\\"2\\\", \\\"4294967294\\\", \\\"4294967295\\\", \\\"01\\\", \\\"word\\\"].includes(key)).join(\\\",\\\")\",ctx));","2,4294967294,0,4294967295,01,word\n"];
        yield return ["for-in","import * as vm from 'node:vm';const ctx:any={1:\"one\",word:\"w\"};console.log(vm.runInNewContext(\"var local;globalThis[2]=\\\"two\\\";globalThis[0]=\\\"zero\\\";const names=[];for(const key in globalThis){if([\\\"0\\\",\\\"1\\\",\\\"2\\\",\\\"local\\\",\\\"word\\\"].includes(key))names.push(key);}names.join(\\\",\\\")\",ctx));","0,2,1,local,word\n"];
        yield return ["values","import * as vm from 'node:vm';const ctx:any={1:\"one\",word:\"w\"};console.log(vm.runInNewContext(\"globalThis[2]=\\\"two\\\";globalThis[0]=\\\"zero\\\";Object.values(globalThis).filter(value=>[\\\"one\\\",\\\"two\\\",\\\"zero\\\",\\\"w\\\"].includes(value)).join(\\\",\\\")\",ctx));","zero,two,one,w\n"];
        yield return ["entries","import * as vm from 'node:vm';const ctx:any={1:\"one\",word:\"w\"};console.log(vm.runInNewContext(\"globalThis[2]=\\\"two\\\";globalThis[0]=\\\"zero\\\";Object.entries(globalThis).filter(pair=>[\\\"0\\\",\\\"1\\\",\\\"2\\\",\\\"word\\\"].includes(pair[0])).map(pair=>pair.join(\\\":\\\")).join(\\\",\\\")\",ctx));","0:zero,2:two,1:one,word:w\n"];
        yield return ["hidden-write","import * as vm from 'node:vm';const ctx:any=Object.defineProperty({first:1},\"hidden\",{value:2,writable:true,configurable:true,enumerable:false});console.log(vm.runInNewContext(\"globalThis.hidden=3;globalThis.added=4;Object.keys(globalThis).filter(key=>[\\\"first\\\", \\\"hidden\\\", \\\"added\\\"].includes(key)).join(\\\",\\\")\",ctx));","added,first\n"];
        yield return ["live-descriptor","import * as vm from 'node:vm';const ctx:any={seed:1};console.log(vm.runInNewContext(\"Object.defineProperty(globalThis,\\\"a\\\",{get(){Object.defineProperty(globalThis,\\\"b\\\",{enumerable:false});return \\\"a\\\";},enumerable:true,configurable:true});globalThis.b=\\\"b\\\";Object.values(globalThis).filter(value=>value===\\\"a\\\"||value===\\\"b\\\").join(\\\",\\\")\",ctx));","a\n"];
        yield return ["keys-no-getters","import * as vm from 'node:vm';const ctx:any={seed:1};console.log(vm.runInNewContext(\"let calls=0;Object.defineProperty(globalThis,\\\"a\\\",{get(){calls++;return 7;},enumerable:true,configurable:true});Object.keys(globalThis).filter(key=>[\\\"a\\\", \\\"seed\\\"].includes(key)).join(\\\",\\\")+\\\":\\\"+calls\",ctx));","a,seed:0\n"];
        yield return ["later-evaluation","import * as vm from 'node:vm';const ctx:any=vm.createContext({1:'one',word:'w'});vm.runInContext('globalThis[2]=\"two\";globalThis.added=3',ctx);console.log(vm.runInContext(\"var local;Object.keys(globalThis).filter(key=>[\\\"1\\\", \\\"2\\\", \\\"word\\\", \\\"added\\\", \\\"local\\\"].includes(key)).join(\\\",\\\")\",ctx));","2,1,added,local,word\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesTheActualInternalAndSandboxKeyOrders(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
