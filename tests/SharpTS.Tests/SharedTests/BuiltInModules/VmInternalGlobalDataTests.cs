using SharpTS.Tests.Infrastructure;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmInternalGlobalDataTests
{
    [Fact]
    public void AnEscapedReaderDoesNotRepublishAnOutsideDeletedProperty()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var context = new SharpTSObject(new Dictionary<string, object?> { ["foo"] = 0d });
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var read = run.Call(caller, ["()=>foo", context]);
        run.Call(caller, ["foo=1", context]);
        Assert.True(context.DeleteProperty("foo"));
        Assert.Equal(1d, VmModuleInterpreter.InvokeVmCallable(read!, SharpTSUndefined.Instance, []));
        Assert.False(context.HasProperty("foo"));
    }

    [Fact]
    public void AnInternalOnlyFunctionCreatedByALaterCallbackKeepsItsActualOwner()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var (context, owner) = CreateLateInternalFunction(caller);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.True(owner.IsAlive);
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var global = Assert.IsType<SharpTSGlobalThis>(run.Call(caller, ["globalThis", context]));
        var stored = global.GetProperty("stored");
        Assert.True(VmModuleInterpreter.IsVmCallableValue(stored));
        var error = VmModuleInterpreter.InvokeVmCallable(stored!, SharpTSUndefined.Instance, []);
        Assert.Contains("internal-owner.ts", VmModuleInterpreter.ReadVmErrorProperty(error!, "stack")?.ToString());
        Assert.Equal(0, caller.ActiveHandleCount);
        GC.KeepAlive(context);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (SharpTSObject Context, WeakReference Owner) CreateLateInternalFunction(Interpreter caller)
    {
        WeakReference? observed = null;
        var context = new SharpTSObject(new Dictionary<string, object?>
        {
            ["capture"] = BuiltInMethod.CreateV2("capture", 0, (owner, _, _) =>
            {
                observed = new WeakReference(owner);
                return RuntimeValue.Undefined;
            })
        });
        context.PreventExtensions();
        var run = Assert.IsAssignableFrom<ISharpTSCallable>(VmModuleInterpreter.GetExports()["runInNewContext"]);
        var options = new Dictionary<string, object?> { ["filename"] = "internal-owner.ts" };
        var maker = run.Call(caller, ["()=>{capture();globalThis.stored=()=>new Error('owned');}", context, options]);
        VmModuleInterpreter.InvokeVmCallable(maker!, SharpTSUndefined.Instance, []);
        return (context, Assert.IsType<WeakReference>(observed));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "import * as vm from \"node:vm\";const ctx:any=vm.createContext({});vm.runInContext(\"globalThis.foo=1\",ctx);ctx.foo=7;console.log(vm.runInContext(\"String(foo)\",ctx));delete ctx.foo;console.log(vm.runInContext(\"String(globalThis.foo)\",ctx));", "7\n1\n"];
        yield return ["successive-writes", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('globalThis.foo=1',ctx);vm.runInContext('foo=3',ctx);ctx.foo=7;delete ctx.foo;console.log(vm.runInContext('String(foo)',ctx));", "3\n"];
        yield return ["inside-delete", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('globalThis.foo=1',ctx);ctx.foo=7;console.log(vm.runInContext('String(delete globalThis.foo)+\":\"+typeof foo',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "true:undefined false\n"];
        yield return ["var-delete", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('var foo=1',ctx);ctx.foo=7;console.log(vm.runInContext('String(delete globalThis.foo)+\":\"+String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "false:1 false\n"];
        yield return ["defined-flags", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('Object.defineProperty(globalThis,\"foo\",{value:1,writable:false,enumerable:false,configurable:true})',ctx);delete ctx.foo;console.log(vm.runInContext('const d=Object.getOwnPropertyDescriptor(globalThis,\"foo\");[d.value,d.writable,d.enumerable,d.configurable].join(\":\")',ctx));", "1:false:false:true\n"];
        yield return ["hidden-existing", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});Object.defineProperty(ctx,'foo',{value:7,writable:true,enumerable:false,configurable:true});vm.runInContext('foo=9',ctx);delete ctx.foo;console.log(vm.runInContext('const d=Object.getOwnPropertyDescriptor(globalThis,\"foo\");[d.value,d.writable,d.enumerable,d.configurable].join(\":\")',ctx));", "9:true:false:true\n"];
        yield return ["readonly-rejected", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});Object.defineProperty(ctx,'foo',{value:7,writable:false,enumerable:true,configurable:true});vm.runInContext('foo=9',ctx);delete ctx.foo;console.log(vm.runInContext('typeof foo+\":\"+String(globalThis.foo)',ctx));", "undefined:undefined\n"];
        yield return ["readonly-internal", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('Object.defineProperty(globalThis,\"foo\",{value:1,writable:false,enumerable:false,configurable:true})',ctx);Object.defineProperty(ctx,'foo',{writable:true,enumerable:true});vm.runInContext('foo=9',ctx);console.log(ctx.foo);delete ctx.foo;console.log(vm.runInContext('String(foo)',ctx));try{vm.runInContext('\"use strict\";foo=9',ctx)}catch(e){console.log((e as Error).name);}", "1\n1\nTypeError\n"];
        yield return ["nonextensible-write", "import * as vm from 'node:vm';const ctx:any=vm.createContext(Object.preventExtensions({}));vm.runInContext('globalThis.foo=1',ctx);console.log(vm.runInContext('String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "1 false\n"];
        yield return ["nonextensible-define", "import * as vm from 'node:vm';const ctx:any=vm.createContext(Object.preventExtensions({}));vm.runInContext('Object.defineProperty(globalThis,\"foo\",{value:1,writable:true,enumerable:true,configurable:true})',ctx);console.log(vm.runInContext('String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "1 false\n"];
        yield return ["define-existing", "import * as vm from 'node:vm';const ctx:any=vm.createContext({foo:7});vm.runInContext('Object.defineProperty(globalThis,\"foo\",{value:1})',ctx);delete ctx.foo;console.log(vm.runInContext('const d=Object.getOwnPropertyDescriptor(globalThis,\"foo\");[d.value,d.writable,d.enumerable,d.configurable].join(\":\")',ctx));", "1:true:true:true\n"];
        yield return ["var-define-update", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('var foo=1;Object.defineProperty(globalThis,\"foo\",{value:9})',ctx);delete ctx.foo;console.log(vm.runInContext('const d=Object.getOwnPropertyDescriptor(globalThis,\"foo\");[d.value,d.writable,d.enumerable,d.configurable].join(\":\")',ctx));", "9:true:true:true\n"];
        yield return ["same-evaluation-var-delete", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var foo=1;String(delete globalThis.foo)+\":\"+String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "false:1 false\n"];
        yield return ["delete-then-write", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});console.log(vm.runInContext('var foo=1;delete globalThis.foo;foo=3;String(foo)',ctx),ctx.foo);", "3 3\n"];
        yield return ["outside-callback-override", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});ctx.change=()=>{ctx.foo=7;};vm.runInContext('globalThis.foo=1;change()',ctx);console.log(ctx.foo,vm.runInContext('String(foo)',ctx));delete ctx.foo;console.log(vm.runInContext('String(foo)',ctx));", "7 7\n1\n"];
        yield return ["seeded-var-uninitialized", "import * as vm from 'node:vm';const ctx:any=vm.createContext({foo:7});console.log(vm.runInContext('var foo;String(delete globalThis.foo)+\":\"+String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "false:undefined false\n"];
        yield return ["seeded-var-initialized", "import * as vm from 'node:vm';const ctx:any=vm.createContext({foo:7});console.log(vm.runInContext('var foo=1;String(delete globalThis.foo)+\":\"+String(foo)',ctx),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "false:1 false\n"];
        yield return ["seeded-var-outside-delete", "import * as vm from 'node:vm';const ctx:any=vm.createContext({foo:7});vm.runInContext('var foo;',ctx);delete ctx.foo;console.log(vm.runInContext('String(foo)',ctx));", "undefined\n"];
        yield return ["escaped-reader-delete", "import * as vm from 'node:vm';const ctx:any=vm.createContext({foo:0});const read:any=vm.runInContext('()=>foo',ctx);vm.runInContext('foo=1',ctx);delete ctx.foo;console.log(read(),Object.prototype.hasOwnProperty.call(ctx,'foo'));", "1 false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RetainsSuccessfulInternalWritesWithoutRetainingItsEvaluator(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
