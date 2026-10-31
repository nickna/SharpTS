using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectMissingArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["zero-arguments", "const get:any=Reflect.get,set:any=Reflect.set,own:any=Reflect.getOwnPropertyDescriptor;for(const method of [get,set,own]){for(const call of [()=>method(),()=>method.call(null),()=>method.apply(null,[]),()=>method.bind(null)()]){try{call();console.log('returned');}catch(error){console.log(error instanceof TypeError);}}}", string.Concat(Enumerable.Repeat("true\n", 12)), false];
        yield return ["missing-key", "const get:any=Reflect.get,own:any=Reflect.getOwnPropertyDescriptor;const target:any={undefined:7};console.log(get(target),own(target).value,own(target).writable);console.log(get(target,undefined),own(target,undefined).value);", "7 7 true\n7 7\n", true];
        yield return ["missing-value", "const set:any=Reflect.set;const target:any={};console.log(set(target),Object.prototype.hasOwnProperty.call(target,'undefined'),typeof target.undefined);console.log(set(target,'slot'),Object.prototype.hasOwnProperty.call(target,'slot'),typeof target.slot);", "true true undefined\ntrue true undefined\n", true];
        yield return ["public-metadata", "for(const method of [Reflect.get,Reflect.set,Reflect.getOwnPropertyDescriptor])console.log(method.name,method.length);const get:any=Reflect.get,set:any=Reflect.set,own:any=Reflect.getOwnPropertyDescriptor;console.log(get.bind(null).length,get.bind(null,{}).length,get.bind(null,{},'x').length,set.bind(null).length,set.bind(null,{}).length,set.bind(null,{},'x',1).length,own.bind(null).length,own.bind(null,{}).length);", "get 2\nset 3\ngetOwnPropertyDescriptor 2\n2 1 0 3 2 0 2 1\n", true];
        yield return ["surplus-expression-order", "const events:string[]=[];const key:any={[Symbol.toPrimitive](){events.push('key');return 'x';}};function extra(){events.push('extra');return 1;}const get:any=Reflect.get,set:any=Reflect.set,own:any=Reflect.getOwnPropertyDescriptor;for(const call of [()=>get(undefined,key,extra(),extra()),()=>set(undefined,key,1,extra(),extra()),()=>own(undefined,key,extra())]){try{call();}catch(error){console.log(error instanceof TypeError,events.join(','));events.length=0;}}", "true extra,extra\ntrue extra,extra\ntrue extra\n", true];
        yield return ["receiver-default", "const get:any=Reflect.get,set:any=Reflect.set;const target:any={};Object.defineProperty(target,'slot',{get:function(this:any){'use strict';return this;},set:function(this:any,value:any){'use strict';console.log(this===target,value===undefined);}});console.log(get(target,'slot')===target,get(target,'slot',undefined)===undefined);console.log(set(target,'slot'),set(target,'slot',undefined,undefined));", "true true\ntrue true\nfalse true\ntrue true\n", true];
        yield return ["symbol-surplus", "let keys=0,extras=0;const symbol=Symbol('slot'),target:any={};const key:any={[Symbol.toPrimitive](){keys++;return symbol;}};function extra(){extras++;return 0;}const get:any=Reflect.get,set:any=Reflect.set,own:any=Reflect.getOwnPropertyDescriptor;console.log(set(target,key,9,target,extra()),get(target,key,target,extra()),own(target,key,extra()).value,keys,extras);", "true 9 9 3 3\n", true];
        yield return ["proxy-missing-key", "const events:string[]=[];const proxy=new Proxy({},{get(target:any,key:any){events.push('get:'+String(key));return 7;},set(target:any,key:any,value:any){events.push('set:'+String(key)+':'+String(value));return true;},getOwnPropertyDescriptor(target:any,key:any){events.push('own:'+String(key));return {value:8,configurable:true};}});const get:any=Reflect.get,set:any=Reflect.set,own:any=Reflect.getOwnPropertyDescriptor;console.log(get(proxy),set(proxy),own(proxy).value,events.join(','));", "7 true 8 get:undefined,set:undefined:undefined,own:undefined\n", false];
        yield return ["awaited-spread", "async function target(){await Promise.resolve();return {undefined:7};}async function run(){const get:any=Reflect.get,set:any=Reflect.set,own:any=Reflect.getOwnPropertyDescriptor;const object:any=await target();const args:[any]=[object];console.log(get(...args),own(...args).value,set(...args),typeof object.undefined);}run();", "7 7 true undefined\n", true];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        ((bool)row[3] ? new[] { "interpreted", "compiled", "native-prepared" } : new[] { "interpreted", "compiled" })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PublicMethodsApplyDefaultsBeforeTheirOwnValidation(string name, string source, string expected, string mode)
    {
        Assert.NotEmpty(name);
        if (mode == "interpreted") Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else if (mode == "compiled")
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var statements = TestHarness.ParseOrThrow(source);
            var types = new TypeChecker().Check(statements);
            var compiler = new ILCompiler("reflect_missing_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
            compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
            var bytes = compiler.SaveToBytes();
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
            Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
        }
    }
}
