using SharpTS.Tests.Infrastructure;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmPersistentContextDataTests
{
    [Fact]
    public void RetainingOnlyTheContextDoesNotRetainItsCompletedEvaluator()
    {
        var (context, evaluator) = EvaluateAndRelease();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(evaluator.IsAlive);
        GC.KeepAlive(context);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (SharpTSObject Context, WeakReference Evaluator) EvaluateAndRelease()
    {
        WeakReference? observed = null;
        var context = new SharpTSObject(new Dictionary<string, object?>
        {
            ["capture"] = BuiltInMethod.CreateV2("capture", 0, (interpreter, _, _) =>
            {
                observed = new WeakReference(interpreter);
                return RuntimeValue.Undefined;
            })
        });
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Assert.Equal(1d, VmModuleInterpreter.ExecuteInNewContext("capture();1", context, caller));
        return (context, Assert.IsType<WeakReference>(observed));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const f:any=vm.runInContext('()=>value',ctx);vm.runInContext('globalThis.value=7',ctx);console.log(f());", "7\n"];
        yield return ["outside-overwrite", "import * as vm from 'node:vm';const ctx:any=vm.createContext({value:1});const f:any=vm.runInContext('()=>value',ctx);ctx.value=7;console.log(f());", "7\n"];
        yield return ["outside-new-property", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const f:any=vm.runInContext('()=>value',ctx);ctx.value=7;console.log(f());", "7\n"];
        yield return ["outside-delete", "import * as vm from 'node:vm';const ctx:any=vm.createContext({value:1});const f:any=vm.runInContext('()=>value',ctx);delete ctx.value;try{f()}catch(e){console.log((e as Error).name);}", "ReferenceError\n"];
        yield return ["var-fallback", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const f:any=vm.runInContext('var value=1;()=>value',ctx);console.log(f());ctx.value=7;console.log(f());delete ctx.value;console.log(f());", "1\n7\n1\n"];
        yield return ["global-identity", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const a:any=vm.runInContext('globalThis',ctx);const b:any=vm.runInContext('globalThis',ctx);console.log(a===b,a===ctx);", "true false\n"];
        yield return ["intrinsic-identity", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});const a:any=vm.runInContext('Math',ctx);const b:any=vm.runInContext('Math',ctx);console.log(a===b);", "true\n"];
        yield return ["readonly-data", "import * as vm from 'node:vm';const ctx:any={};Object.defineProperty(ctx,'value',{value:7,writable:false,enumerable:true,configurable:true});console.log(vm.runInNewContext('value=9;value',ctx));try{vm.runInNewContext('\"use strict\";value=9',ctx)}catch(e){console.log((e as Error).name);}console.log(ctx.value);", "7\nTypeError\n7\n"];
        yield return ["outer-accessor-receiver", "import * as vm from 'node:vm';let backing=1;let events='';const ctx:any={};Object.defineProperty(ctx,'value',{get(){events+='g'+String(this===ctx);return backing;},set(v:any){events+='s'+String(this===ctx)+String(v);backing=v;},configurable:true,enumerable:true});console.log(vm.runInNewContext('value=7;value',ctx),events);", "7 strue7gtrue\n"];
        yield return ["inner-accessor-receiver", "import * as vm from 'node:vm';const ctx:any={};console.log(vm.runInNewContext('Object.defineProperty(globalThis,\"value\",{get(){return this===globalThis},configurable:true});value',ctx));", "false\n"];
        yield return ["data-descriptor", "import * as vm from 'node:vm';const ctx:any={};Object.defineProperty(ctx,'value',{value:7,writable:false,enumerable:true,configurable:true});const g:any=vm.runInNewContext('globalThis',ctx);const d:any=Object.getOwnPropertyDescriptor(g,'value');console.log(d.value,d.writable,d.enumerable,d.configurable);", "7 false true true\n"];
        yield return ["callback-identity", "import * as vm from 'node:vm';const ctx:any={fn:()=>7};const f:any=vm.runInNewContext('()=>fn',ctx);console.log(f()===ctx.fn);", "true\n"];
        yield return ["context-isolation", "import * as vm from 'node:vm';const a:any={value:7},b:any={value:9};const fa:any=vm.runInNewContext('()=>value',a),fb:any=vm.runInNewContext('()=>value',b);console.log(fa(),fb());", "7 9\n"];
        yield return ["var-unpublished-descriptor", "import * as vm from 'node:vm';const ctx:any={};const g:any=vm.runInNewContext('var value;globalThis',ctx);const d:any=Object.getOwnPropertyDescriptor(g,'value');console.log('value' in ctx,d.value===undefined,d.writable,d.enumerable,d.configurable);", "false true true true false\n"];
        yield return ["var-published-descriptor", "import * as vm from 'node:vm';const ctx:any={};const g:any=vm.runInNewContext('var value=7;globalThis',ctx);const d:any=Object.getOwnPropertyDescriptor(g,'value');console.log(ctx.value,d.value,d.writable,d.enumerable,d.configurable);", "7 7 true true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void UsesLiveContextDataWithoutRetainingItsEvaluator(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }
}
