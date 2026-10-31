using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmNativeInputWrite3262Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-index-write", "import * as vm from 'node:vm';const value:any=[1,2];console.log(vm.runInNewContext('value[0]=7;value[0]',{value}),value[0]);", "7 7\n"];
        yield return ["sloppy-refusal-result", "import * as vm from 'node:vm';const value:any=[1];Object.defineProperty(value,'0',{value:1,writable:false,configurable:true});console.log(vm.runInNewContext('value[0]=7',{value}),value[0]);", "7 1\n"];
        yield return ["strict-refusal", "import * as vm from 'node:vm';const value:any=[1];Object.defineProperty(value,'0',{value:1,writable:false,configurable:true});try{vm.runInNewContext(\"'use strict';value[0]=7\",{value});}catch(error){console.log((error as Error).name);}console.log(value[0]);", "TypeError\n1\n"];
        yield return ["setter-receiver-once", "import * as vm from 'node:vm';const value:any=[0];let stored=0,calls=0,receiver=false;Object.defineProperty(value,'0',{get(){return stored;},set(v){calls++;receiver=this===value;stored=v;},configurable:true});console.log(vm.runInNewContext('value[0]=7;value[0]',{value}),calls,receiver);", "7 1 true\n"];
        yield return ["setter-abrupt-identity", "import * as vm from 'node:vm';const value:any=[0];const marker={};let calls=0;Object.defineProperty(value,'0',{set(v){calls++;throw marker;},configurable:true});try{vm.runInNewContext('value[0]=7',{value});}catch(error){console.log(error===marker,calls);}", "true 1\n"];
        yield return ["key-rhs-order", "import * as vm from 'node:vm';const value:any=[0];const log:string[]=[];console.log(vm.runInNewContext(\"const key={[Symbol.toPrimitive](hint){note(hint);return '0';}};function rhs(){note('rhs');return 7;}value[key]=rhs();value[0]\",{value,note:(v:any)=>log.push(v)}),log.join(','));", "7 rhs,string\n"];
        yield return ["incoming-value-identity", "import * as vm from 'node:vm';const value:any=[0];const marker={};console.log(vm.runInNewContext('value[0]=marker;value[0]===marker',{value,marker}),value[0]===marker);", "true true\n"];
        yield return ["frozen-length-strict", "import * as vm from 'node:vm';const value:any=Object.freeze([1,2]);try{vm.runInNewContext(\"'use strict';value.length=0\",{value});}catch(error){console.log((error as Error).name);}console.log(value.length);", "TypeError\n2\n"];
    }
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));
    [Theory, MemberData(nameof(Modes))]
    public void WritesThroughTheActualInputEndpoint(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
