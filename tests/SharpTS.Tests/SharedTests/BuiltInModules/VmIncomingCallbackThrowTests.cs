using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmIncomingCallbackThrowTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["caught-object-assignment", "import * as vm from \"node:vm\";const marker:any={x:1};const fn:any=()=>{throw marker;};console.log(vm.runInNewContext(\"try{fn()}catch(error){result=error===marker;}result;\",{fn,marker,result:false}));", "true\n"];
        yield return ["caught-undefined-assignment", "import * as vm from \"node:vm\";const fn:any=()=>{throw undefined;};console.log(vm.runInNewContext(\"try{fn()}catch(error){result=typeof error;}result;\",{fn,result:\"unset\"}));", "undefined\n"];
        yield return ["callback-throws-2", "import * as vm from 'node:vm';const value:any=new Error('outside');const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
        yield return ["callback-throws-3", "import * as vm from 'node:vm';const value:any=Symbol('key');const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
        yield return ["callback-throws-4", "import * as vm from 'node:vm';const value:any='guest-string';const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
        yield return ["callback-throws-5", "import * as vm from 'node:vm';const value:any=7;const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
        yield return ["callback-throws-6", "import * as vm from 'node:vm';const value:any=true;const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
        yield return ["callback-throws-7", "import * as vm from 'node:vm';const value:any=null;const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
        yield return ["callback-throws-8", "import * as vm from 'node:vm';const value:any=()=>7;const fn:any=()=>{throw value;};console.log(vm.runInNewContext('try{fn()}catch(error){result=error===value;}result;',{fn,value,result:false}));", "true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void IncomingCallbackThrowsKeepTheirExactPayload(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}