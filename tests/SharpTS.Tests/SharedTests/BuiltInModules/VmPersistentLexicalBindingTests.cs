using SharpTS.Tests.Infrastructure;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmPersistentLexicalBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["const-read-second-run", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('const value=7;',ctx);console.log(vm.runInContext('value;',ctx));", "7\n"];
        yield return ["let-mutation-third-run", "import * as vm from 'node:vm';const ctx:any=vm.createContext({});vm.runInContext('let value=7;',ctx);vm.runInContext('value++;',ctx);console.log(vm.runInContext('value;',ctx),Object.prototype.hasOwnProperty.call(ctx,'value'));", "8 false\n"];
        yield return ["completed-vm-definition-releases-var-restriction", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('var value=1;Object.defineProperty(globalThis,\"value\",{value:9})',c);vm.runInContext('let value=2',c);console.log(vm.runInContext('value',c));", "2\n"];
        yield return ["outside-definition-retains-var-restriction", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('var value=1',c);Object.defineProperty(c,'value',{value:9,configurable:true});try{vm.runInContext('let value=2',c)}catch(e){console.log((e as Error).name)}console.log(vm.runInContext('value',c));", "SyntaxError\n9\n"];
        yield return ["completed-vm-definition-releases-function-restriction", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('function value(){};Object.defineProperty(globalThis,\"value\",{value:9})',c);vm.runInContext('let value=2',c);console.log(vm.runInContext('value',c));", "2\n"];
        yield return ["source-binding-and-generated-spill", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('let _dest0=7',c);vm.runInContext('const [value]=[9]',c);console.log(vm.runInContext('_dest0+value',c));", "16\n"];
        yield return ["same-run-source-binding-and-spill", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('let _dest0=7;const [value]=[9]',c);console.log(vm.runInContext('_dest0+value',c));", "16\n"];
        yield return ["destructuring-source-bindings-only", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('const [first]=[7]',c);vm.runInContext('const [second]=[9]',c);console.log(vm.runInContext('first+second',c));try{vm.runInContext('first=1',c)}catch(e){console.log((e as Error).name)}", "16\nTypeError\n"];
        yield return ["source-name-resembling-spill", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('let _dest0=7',c);console.log(vm.runInContext('_dest0',c));", "7\n"];
        yield return ["function-then-let", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('function value(){}',c);try{vm.runInContext('let value=9',c)}catch(e){console.log((e as Error).name)}", "SyntaxError\n"];
        yield return ["earlier-closure-sees-new-binding", "import * as vm from 'node:vm';const c:any=vm.createContext({});const f:any=vm.runInContext('()=>value',c);vm.runInContext('let value=9',c);console.log(f());", "9\n"];
        yield return ["later-update-reaches-earlier-closure", "import * as vm from 'node:vm';const c:any=vm.createContext({});const f:any=vm.runInContext('let value=7;()=>++value',c);vm.runInContext('value=9',c);console.log(f(),vm.runInContext('value',c));", "10 10\n"];
        yield return ["const-write-rejected", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('const value=7',c);try{vm.runInContext('value=9',c)}catch(e){console.log((e as Error).name)}console.log(vm.runInContext('value',c));", "TypeError\n7\n"];
        yield return ["duplicate-lexical", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('let value=7',c);try{vm.runInContext('const value=9',c)}catch(e){console.log((e as Error).name)}console.log(vm.runInContext('value',c));", "SyntaxError\n7\n"];
        yield return ["nested-var-conflict", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('let value=7',c);try{vm.runInContext('if(false){var value=9}',c)}catch(e){console.log((e as Error).name)}console.log(vm.runInContext('value',c));", "SyntaxError\n7\n"];
        yield return ["var-then-let", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('var value=7',c);try{vm.runInContext('let value=9',c)}catch(e){console.log((e as Error).name)}console.log(vm.runInContext('value',c));", "SyntaxError\n7\n"];
        yield return ["readonly-sandbox-shadow", "import * as vm from 'node:vm';const c:any=vm.createContext({});Object.defineProperty(c,'value',{value:1});vm.runInContext('let value=7',c);console.log(vm.runInContext('value',c),c.value);", "7 1\n"];
        yield return ["nonextensible-context", "import * as vm from 'node:vm';const c:any=vm.createContext(Object.preventExtensions({}));vm.runInContext('let value=7',c);console.log(vm.runInContext('value',c),'value' in c);", "7 false\n"];
        yield return ["failed-initializer-tdz", "import * as vm from 'node:vm';const c:any=vm.createContext({});try{vm.runInContext('let value=missing',c)}catch(e){console.log((e as Error).name)}try{vm.runInContext('typeof value',c)}catch(e){console.log((e as Error).name)}", "ReferenceError\nReferenceError\n"];
        yield return ["class-persistence", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('class Value {}',c);console.log(vm.runInContext('typeof Value',c),'Value' in c);", "function false\n"];
        yield return ["strict-script-persistence", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('\"use strict\";let value=7',c);console.log(vm.runInContext('value',c));", "7\n"];
        yield return ["separate-contexts", "import * as vm from 'node:vm';const a:any=vm.createContext({}),b:any=vm.createContext({});vm.runInContext('let value=7',a);vm.runInContext('let value=9',b);console.log(vm.runInContext('value',a),vm.runInContext('value',b));", "7 9\n"];
        yield return ["parsed-script-entry", "import * as vm from 'node:vm';const c:any=vm.createContext({});new vm.Script('let value=7').runInContext(c);console.log(new vm.Script('++value').runInContext(c),new vm.Script('value').runInContext(c));", "8 8\n"];
        yield return ["preflight-does-not-reserve-partial-list", "import * as vm from 'node:vm';const c:any=vm.createContext({});vm.runInContext('let prior=7',c);try{vm.runInContext('let fresh=1;let prior=2',c)}catch(e){console.log((e as Error).name)}vm.runInContext('let fresh=9',c);console.log(vm.runInContext('fresh',c));", "SyntaxError\n9\n"];
        yield return ["restricted-intrinsic", "import * as vm from 'node:vm';const c:any=vm.createContext({undefined:7});try{vm.runInContext('let undefined=9',c)}catch(e){console.log((e as Error).name)}", "SyntaxError\n"];



    }

    [Theory, MemberData(nameof(Cases))]
    public void LexicalBindingsPersistWithoutBecomingContextProperties(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }

    [Fact]
    public void PrimitiveAndInputLexicalsDoNotRetainTheirCompletedEvaluator()
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
        Assert.Equal(7d, VmModuleInterpreter.ExecuteInNewContext("capture();const input=capture;let value=7;value", context, caller));
        Assert.Equal(8d, VmModuleInterpreter.ExecuteInNewContext("++value", context, caller));
        Assert.False(context.HasProperty("input"));
        return (context, Assert.IsType<WeakReference>(observed));
    }

    [Fact]
    public void AStoredLexicalCallableRetainsItsCreationOwner()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var context = new SharpTSObject(new Dictionary<string, object?>());
        VmModuleInterpreter.ExecuteInNewContext("const fn=()=>{throw new Error('lexical-owner')};0", context, caller, filename: "lexical-origin.js");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var callable = VmModuleInterpreter.ExecuteInNewContext("fn", context, caller);
        Assert.True(VmModuleInterpreter.IsVmCallableValue(callable));
        var thrown = Assert.Throws<SharpTS.Runtime.Exceptions.ThrowException>(() => VmModuleInterpreter.InvokeVmCallable(callable!, null, []));
        Assert.Contains("lexical-origin.js", Assert.IsType<string>(VmModuleInterpreter.ReadVmErrorProperty(thrown.Value!, "stack")));
    }
}