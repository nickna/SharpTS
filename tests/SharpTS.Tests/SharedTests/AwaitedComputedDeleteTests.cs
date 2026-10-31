using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AwaitedComputedDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const symbol=Symbol(\"key\"),target:any={};target[symbol]=7;const trace:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){trace.push(hint);return symbol;}};async function receiver(){trace.push(\"receiver\");return target;}async function property(){trace.push(\"key\");return key;}async function run(){console.log(delete(await receiver())[await property()],Object.hasOwn(target,symbol),trace.join(\",\"));}run();\n", "true false receiver,key,string\n"];
        yield return ["key-only", "async function run(){const target:any={x:7};console.log(delete target[await Promise.resolve('x')],Object.hasOwn(target,'x'));}run();", "true false\n"];
        yield return ["receiver-only", "async function run(){const target:any={x:7};console.log(delete(await Promise.resolve(target))['x'],Object.hasOwn(target,'x'));}run();", "true false\n"];
        yield return ["timer-and-reassigned-receiver", "const events:string[]=[];const original:any={x:7},replacement:any={x:8};let target:any=original;function receiver(){events.push('receiver');return target;}async function property(){events.push('key');await new Promise<void>(resolve=>setTimeout(()=>{target=replacement;events.push('timer');resolve();},1));return {[Symbol.toPrimitive](hint:string){events.push(hint);return 'x';}} as any;}async function run(){console.log(delete receiver()[await property()],Object.hasOwn(original,'x'),replacement.x,events.join(','));}run();", "true false 8 receiver,key,timer,string\n"];
        yield return ["abrupt-await", "const events:string[]=[];const target:any={x:7};function receiver(){events.push('receiver');return target;}async function property():Promise<any>{events.push('key');await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));throw new Error('stop');}async function run(){try{delete receiver()[await property()];}catch(error){console.log((error as any).message,target.x,events.join(','));}}run();", "stop 7 receiver,key\n"];
        yield return ["strict-symbol", "'use strict';const symbol=Symbol('x'),target:any={};target[symbol]=7;let calls=0;async function run(){const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return symbol;}};console.log(delete target[await Promise.resolve(key)],Object.hasOwn(target,symbol),calls);}run();", "string\ntrue false 1\n"];
        yield return ["sloppy-refusal", "const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});async function run(){console.log(delete target[await Promise.resolve('x')],target.x);}run();", "false 7\n"];
        yield return ["strict-refusal", "'use strict';const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});async function run(){try{delete target[await Promise.resolve('x')];}catch(error){console.log(error instanceof TypeError,target.x);}}run();", "true 7\n"];
        yield return ["yielded-key", "const original:any={x:7},replacement:any={x:8};let target:any=original;function* remove():Generator<any,any,any>{console.log(delete target[yield 'key']);}const iterator=remove();console.log(iterator.next().value);target=replacement;console.log(iterator.next('x').done,Object.hasOwn(original,'x'),replacement.x);", "key\ntrue\ntrue false 8\n"];
        yield return ["abrupt-conversion", "const target:any={x:7};let calls=0;async function run(){const key:any={[Symbol.toPrimitive](){calls++;throw new Error('coercion');}};try{delete target[await Promise.resolve(key)];}catch(error){console.log((error as any).message,target.x,calls);}}run();", "coercion 7 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { "interpreted", "compiled", "native-prepared" }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesReceiverAndKeyAcrossSuspension(string name, string source, string expected, string mode)
    {
        Assert.NotEmpty(name);
        string actual;
        if (mode == "interpreted") actual = TestHarness.RunInterpreted(source);
        else if (mode == "compiled")
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            Assert.Equal("native-prepared", mode);
            var statements = TestHarness.ParseOrThrow(source);
            var types = new TypeChecker().Check(statements);
            var compiler = new ILCompiler("regression_3160_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
            compiler.EnableNativeAotProjectOutput();
            compiler.Compile(statements, types);
            var bytes = compiler.SaveToBytes();
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
            actual = TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20));
        }
        Assert.Equal(expected, actual);
    }
}
