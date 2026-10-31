using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmCallbackReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-receiver", "import * as vm from 'node:vm';const fn:any=function(this:{x:number}){return this.x;};try{console.log(vm.runInNewContext('fn.call({x:7})',{fn}),vm.runInNewContext('fn.apply({x:8},[])',{fn}));}catch(error){console.log('error');}", "7 8\n"];
        yield return ["receiver-and-arguments", "import * as vm from 'node:vm';const fn:any=function(this:{x:number},a:number,b:number){return this.x+a+b;};console.log(vm.runInNewContext('fn.call({x:7},2,3)',{fn}),vm.runInNewContext('Reflect.apply(fn,{x:8},[2,3])',{fn}));", "12 13\n"];
        yield return ["bound-receiver", "import * as vm from 'node:vm';const fn:any=function(this:{x:number},a:number,b:number){return this.x+a+b;};console.log(vm.runInNewContext('fn.bind({x:7},2)(3)',{fn}));", "12\n"];
        yield return ["strict-null-undefined", "import * as vm from 'node:vm';const fn:any=function(this:any){'use strict';return this===null?'null':this===undefined?'undefined':'other';};console.log(vm.runInNewContext('fn.call(null)',{fn}),vm.runInNewContext('fn.call(undefined)',{fn}));", "null undefined\n"];
        yield return ["receiver-outer-identity", "import * as vm from 'node:vm';const marker:any={x:7};const fn:any=function(this:any){return this===marker;};console.log(vm.runInNewContext('fn.call(marker)',{fn,marker}));", "true\n"];
        yield return ["arrow-ignores-receiver", "import * as vm from 'node:vm';const value=7;const fn:any=()=>value;console.log(vm.runInNewContext('fn.call({x:8})',{fn}),vm.runInNewContext('fn.bind({x:9})()',{fn}));", "7 7\n"];
        yield return ["direct-call-and-callback-identity", "import * as vm from 'node:vm';const fn:any=()=>7;console.log(vm.runInNewContext('fn()',{fn}),vm.runInNewContext('fn',{fn})===fn);", "7 true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SuppliedCallbacksReceiveTheActualCallReceiver(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Fact]
    public void StructuralCompatibilityDoesNotAcquireAReceiverEndpoint()
    {
        var value = new StructuralCallable();
        var adapter = Assert.IsAssignableFrom<ISharpTSCallable>(VmContext.WrapCompiledCallable(value));
        using var interpreter = new SharpTS.Execution.Interpreter();
        Assert.Equal(7, FunctionBuiltIns.CallWithThis(interpreter, adapter, new object(), [7]));
    }

    public sealed class StructuralCallable
    {
        public object Invoke(object[] args) => args[0];
        public object InvokeWithThis(object receiver, object[] args) => throw new InvalidOperationException("Structural receiver admission changed");
    }
}