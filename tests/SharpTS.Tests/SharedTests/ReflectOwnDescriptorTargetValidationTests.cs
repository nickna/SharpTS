using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectOwnDescriptorTargetValidationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "for(const api of ['Object','Reflect'])for(const target of [null,undefined,0,'x',true,1n,Symbol('s')]){\nlet calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;return 'x';}};\ntry{const descriptor=api==='Object'?Object.getOwnPropertyDescriptor(target as any,key):Reflect.getOwnPropertyDescriptor(target as any,key);console.log(api,typeof target,'return',calls);}\ncatch(error){console.log(api,typeof target,error instanceof TypeError,calls);}\n}\r\n", "Object object true 0\nObject undefined true 0\nObject number return 1\nObject string return 1\nObject boolean return 1\nObject bigint return 1\nObject symbol return 1\nReflect object true 0\nReflect undefined true 0\nReflect number true 0\nReflect string true 0\nReflect boolean true 0\nReflect bigint true 0\nReflect symbol true 0\n", true];
        yield return ["retained-call-apply-bind", "const own:any=Reflect.getOwnPropertyDescriptor;for(const target of [null,undefined,false,0,'x',1n,Symbol('s')]){let calls=0;const key:any={[Symbol.toPrimitive](){calls++;throw new Error('key');}};for(const invoke of [()=>own(target,key),()=>own.call(null,target,key),()=>own.apply(null,[target,key]),()=>own.bind(null,target)(key)]){try{invoke();console.log('returned',calls);}catch(error){console.log(error instanceof TypeError,calls);}}}", "true 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\n", true];
        yield return ["argument-expression-order", "const events:string[]=[];function key(){events.push('expression');return {[Symbol.toPrimitive](){events.push('conversion');return 'x';}};}try{Reflect.getOwnPropertyDescriptor(null as any,key() as any);}catch(error){console.log(error instanceof TypeError,events.join(','));}", "true expression\n", true];
        yield return ["valid-symbol-accessor", "let calls=0,reads=0;const symbol=Symbol('x'),target:any={};Object.defineProperty(target,symbol,{get(){reads++;return 7;},configurable:true});const key:any={[Symbol.toPrimitive](){calls++;return symbol;}};const own=Reflect.getOwnPropertyDescriptor;const descriptor:any=own(target,key);console.log(typeof descriptor.get,descriptor.enumerable,descriptor.configurable,calls,reads);", "function false true 1 0\n", true];
        yield return ["awaited-and-spread-arguments", "async function key(){await new Promise<void>(resolve=>setTimeout(()=>resolve(),1));return 'slot';}async function run(){const target:any={slot:7};console.log(Reflect.getOwnPropertyDescriptor(target,await key())!.value);const args:[any,any]=[target,'slot'];console.log(Reflect.getOwnPropertyDescriptor(...args)!.value);}run();", "7\n7\n", true];
        yield return ["proxy-key-order", "const events:string[]=[];const symbol=Symbol('x'),target:any={};Object.defineProperty(target,symbol,{value:7,configurable:true});const proxy=new Proxy(target,{getOwnPropertyDescriptor(object:any,key:any){events.push('trap');return Reflect.getOwnPropertyDescriptor(object,key);}});const key:any={[Symbol.toPrimitive](){events.push('key');return symbol;}};console.log(Reflect.getOwnPropertyDescriptor(proxy,key)!.value,events.join(','));", "7 key,trap\n", false];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        ((bool)row[3] ? new[] { "interpreted", "compiled", "native-prepared" } : new[] { "interpreted", "compiled" })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesThePublishedProgramAndReportedVariants(string name, string source, string expected, string mode)
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
            var compiler = new ILCompiler("public_reflect_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
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
