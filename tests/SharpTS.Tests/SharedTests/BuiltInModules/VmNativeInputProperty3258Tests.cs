using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmNativeInputProperty3258Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["array", "import * as vm from 'node:vm';const log:number[]=[];vm.runInNewContext('log.push(7)',{log});console.log(log.join(','));", "7\n"];
        yield return ["object", "import * as vm from 'node:vm';const value:any={x:7};console.log(vm.runInNewContext('value.x',{value}));", "7\n"];
        yield return ["error", "import * as vm from 'node:vm';const error:any=new Error('outside');error.stack='outside-only';console.log(vm.runInNewContext('error.stack',{error}));", "outside-only\n"];
        yield return ["primitive-control", "import * as vm from 'node:vm';console.log(vm.runInNewContext('value',{value:7}));", "7\n"];
        yield return ["array-members-use-actual-receiver", "import * as vm from 'node:vm';const value:number[]=[];console.log(vm.runInNewContext(\"value.push(7,8);value.join(',')\",{value}),value.join(','));", "7,8 7,8\n"];
        yield return ["array-getter-once", "import * as vm from 'node:vm';const value:any=[];let calls=0;Object.defineProperty(value,'x',{get(){calls++;console.log(this===value);return 7;},configurable:true});console.log(vm.runInNewContext('value.x',{value}),calls);", "true\n7 1\n"];
        yield return ["array-getter-abrupt-identity", "import * as vm from 'node:vm';const value:any=[];const marker={};let calls=0;Object.defineProperty(value,'x',{get(){calls++;throw marker;},configurable:true});try{vm.runInNewContext('value.x',{value});}catch(error){console.log(error===marker,calls);}", "true 1\n"];
        yield return ["input-repeated-identity", "import * as vm from 'node:vm';const value:any=[1,2];const ctx:any=vm.createContext({value,alias:value});console.log(vm.runInContext('value===alias',ctx),vm.runInContext('value',ctx)===value);", "true true\n"];
        yield return ["array-numeric-read", "import * as vm from 'node:vm';const value:any=[1,2];console.log(vm.runInNewContext('value[0]',{value}));", "1\n"];
        yield return ["borrowed-receiver", "import * as vm from 'node:vm';const value:any=[];const other:any=[];console.log(vm.runInNewContext(\"value.push.call(other,7);other.join(',')\",{value,other}),value.length,other[0]);", "7 0 7\n"];
        yield return ["callback-method", "import * as vm from 'node:vm';const value:any=[1,2];console.log(vm.runInNewContext(\"value.map((x)=>x+1).join(',')\",{value}));", "2,3\n"];
        yield return ["precomputed-map-input", "import * as vm from 'node:vm';const value=[1,2].map(x=>x+1);console.log(vm.runInNewContext(\"value.join(',')\",{value}));", "2,3\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Fact]
    public void PreservesCompiledExtractedMethodIdentity()
    {
        const string source = "import * as vm from 'node:vm';const value:any=[];const fn:any=vm.runInNewContext('value.push',{value});fn.call(value,7);console.log(typeof fn,fn===value.push,value.join(','));";
        Assert.Equal("function true 7\n", TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(Modes))]
    public void ReadsOnlyActualNativeInputs(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
